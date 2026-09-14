using Coon.Meeting.Api.Config;
using Coon.Meeting.Api.Models;
using LiteDB;

namespace Coon.Meeting.Api.Repositories;

/// <summary>
/// Owns the single LiteDatabase instance for the process's lifetime - LiteDB is safe for
/// many concurrent operations against one open instance, but not for two separate processes
/// (or two separate instances in this one process) writing to the same file at once.
/// </summary>
public class LiteDbContext : IDisposable
{
    private readonly LiteDatabase _db;

    public LiteDbContext(DatabaseSettings settings)
    {
        var dir = Path.GetDirectoryName(settings.FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        BsonMapper.Global.EnumAsInteger = false;

        // LiteDB otherwise converts DateTime back to the server's local time zone on read
        // (via .ToLocalTime()), silently turning every "UTC" field in the models into a
        // Local-kind value - fine for a single fixed-offset server, but a correctness trap
        // once ICS generation and the reminder poller depend on ScheduledAt actually being UTC.
        // bson.AsDateTime comes back already shifted by LiteDB's own ToLocalTime() - converting
        // it (not just re-tagging it) back to UTC is what actually undoes that shift.
        BsonMapper.Global.RegisterType(
            serialize: (DateTime dt) => dt.ToUniversalTime(),
            deserialize: (bson) => bson.AsDateTime.ToUniversalTime());

        _db = new LiteDatabase(settings.FilePath);

        EnsureIndexes();
    }

    public ILiteCollection<Tenant> Tenants => _db.GetCollection<Tenant>("tenants");
    public ILiteCollection<Models.Meeting> Meetings => _db.GetCollection<Models.Meeting>("meetings");
    public ILiteCollection<WebhookDelivery> WebhookDeliveries => _db.GetCollection<WebhookDelivery>("webhookDeliveries");

    /// <summary>
    /// Atomically finds one meeting starting within <paramref name="window"/> that hasn't had
    /// its reminder sent yet, and marks it sent in the same transaction - so a poll tick can't
    /// claim the same meeting twice. Returns null when there's nothing due.
    /// </summary>
    /// <remarks>
    /// Unlike the Mongo-backed reference (FindOneAndUpdate, safe across several API instances),
    /// this only has to be safe within one process - the single-instance trade-off documented
    /// at the top of the project plan. A BeginTrans/Commit pair is still used, not because two
    /// concurrent pollers could otherwise race (there's only ever one), but because an HTTP
    /// request resetting ReminderSentAt (MeetingsController.Update, on reschedule) could
    /// otherwise interleave between this method's read and its write.
    /// </remarks>
    public Models.Meeting? ClaimMeetingNeedingReminder(DateTime now, TimeSpan window)
    {
        // LiteDB's LINQ-to-BsonExpression translator can't convert a method call like
        // now.Add(window) inside the predicate - it only understands member/constant access -
        // so the upper bound has to be a plain captured value, computed before the query.
        var until = now.Add(window);

        _db.BeginTrans();
        try
        {
            var due = Meetings.Find(m =>
                    m.Status == MeetingStatus.Scheduled &&
                    m.ScheduledAt > now &&
                    m.ScheduledAt <= until &&
                    m.ReminderSentAt == null)
                .FirstOrDefault();

            if (due != null)
            {
                due.ReminderSentAt = now;
                Meetings.Update(due);
            }

            _db.Commit();
            return due;
        }
        catch
        {
            _db.Rollback();
            throw;
        }
    }

    private void EnsureIndexes()
    {
        Tenants.EnsureIndex(t => t.ApiKeyHash, unique: true);

        Meetings.EnsureIndex(m => m.TenantId);
        Meetings.EnsureIndex("idx_tenant_scheduled", "[$.TenantId, $.ScheduledAt]");
        Meetings.EnsureIndex("idx_reminder_due", "[$.ScheduledAt, $.ReminderSentAt, $.Status]");

        WebhookDeliveries.EnsureIndex("idx_status_next_attempt", "[$.Status, $.NextAttemptAt]");
    }

    public void Dispose() => _db.Dispose();
}
