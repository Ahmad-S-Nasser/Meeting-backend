using Coon.Meeting.Api.Models;

namespace Coon.Meeting.Api.Repositories;

public class MeetingRepository : IMeetingRepository
{
    private readonly LiteDbContext _db;

    public MeetingRepository(LiteDbContext db)
    {
        _db = db;
    }

    public Task<List<Models.Meeting>> GetByTenantIdAsync(string tenantId) =>
        Task.FromResult(_db.Meetings.Find(m => m.TenantId == tenantId).ToList());

    public Task<Models.Meeting?> GetByIdAsync(string tenantId, string id)
    {
        var meeting = _db.Meetings.FindById(id);
        return Task.FromResult(meeting != null && meeting.TenantId == tenantId ? meeting : null);
    }

    public Task CreateAsync(Models.Meeting meeting)
    {
        _db.Meetings.Insert(meeting);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Models.Meeting meeting)
    {
        _db.Meetings.Update(meeting);
        return Task.CompletedTask;
    }

    public Task<Models.Meeting?> ClaimMeetingNeedingReminderAsync(DateTime now, TimeSpan window) =>
        Task.FromResult(_db.ClaimMeetingNeedingReminder(now, window));
}
