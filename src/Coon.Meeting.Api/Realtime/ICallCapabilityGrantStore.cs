namespace Coon.Meeting.Api.Realtime;

/// <summary>
/// Call-session-only state for the organizer's live screen-share/recording grants pushed via
/// MeetingCallModerationService.PushCapabilityChangeAsync - kept in memory, matching
/// MeetingRoomRegistry's own "no database, in-memory registry" pattern for live-call state,
/// purely so a grant survives a participant's reconnect mid-call (the room registry itself only
/// tracks live connections, nothing about what was granted). Not persisted anywhere else; a
/// process restart or the meeting simply being over loses it, same as the room registry.
/// </summary>
public interface ICallCapabilityGrantStore
{
    void SetGrant(string meetingId, string participantExternalId, string capability, bool allowed);

    /// <summary>Every capability explicitly granted/revoked for this participant so far this call.</summary>
    IReadOnlyDictionary<string, bool> GetGrants(string meetingId, string participantExternalId);
}
