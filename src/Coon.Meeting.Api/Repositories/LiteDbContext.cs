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

    private void EnsureIndexes()
    {
        Tenants.EnsureIndex(t => t.ApiKeyHash, unique: true);

        Meetings.EnsureIndex(m => m.TenantId);
        Meetings.EnsureIndex("idx_tenant_scheduled", "[$.TenantId, $.ScheduledAt]");
        Meetings.EnsureIndex("idx_reminder_due", "[$.ScheduledAt, $.ReminderSentAt, $.Status]");
    }

    public void Dispose() => _db.Dispose();
}
