using System.Collections.Concurrent;

namespace Coon.Meeting.Api.Realtime;

public class CallCapabilityGrantStore : ICallCapabilityGrantStore
{
    // meetingId -> participantExternalId -> capability -> allowed
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>> _grants = new();

    public void SetGrant(string meetingId, string participantExternalId, string capability, bool allowed)
    {
        var meetingGrants = _grants.GetOrAdd(meetingId, _ => new ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>());
        var participantGrants = meetingGrants.GetOrAdd(participantExternalId, _ => new ConcurrentDictionary<string, bool>());
        participantGrants[capability] = allowed;
    }

    public IReadOnlyDictionary<string, bool> GetGrants(string meetingId, string participantExternalId) =>
        _grants.TryGetValue(meetingId, out var meetingGrants) && meetingGrants.TryGetValue(participantExternalId, out var participantGrants)
            ? participantGrants
            : new Dictionary<string, bool>();
}
