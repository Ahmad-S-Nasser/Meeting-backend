namespace Coon.Meeting.Api.Realtime;

/// <summary>
/// Tracks live call-room membership (meetingId -> connectionId -> CallParticipant) as an
/// injectable singleton, rather than private static state inside MeetingCallHub - a REST
/// controller needs to be able to find "which live connection(s) belong to external id X in
/// meeting Y" to force a kick/block through, and there's no way to reach into a Hub
/// instance's own fields from outside it.
/// </summary>
public interface IMeetingRoomRegistry
{
    /// <summary>Snapshot of who was already in the room, taken BEFORE adding this connection.</summary>
    IReadOnlyCollection<CallParticipant> AddParticipant(string meetingId, string connectionId, string participantId, string name);

    (string? MeetingId, CallParticipant? Participant) RemoveParticipant(string connectionId);

    IEnumerable<string> GetConnectionIdsForParticipant(string meetingId, string participantExternalId);
}
