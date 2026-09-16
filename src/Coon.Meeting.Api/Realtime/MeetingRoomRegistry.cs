using System.Collections.Concurrent;

namespace Coon.Meeting.Api.Realtime;

public class MeetingRoomRegistry : IMeetingRoomRegistry
{
    // meetingId -> (connectionId -> participant). Needed both to hand a joining peer the
    // list of who's already in the room, and to notify the room on a dropped connection
    // without the client having called LeaveCall first.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CallParticipant>> _rooms = new();

    // connectionId -> meetingId, so a disconnect/kick/block knows which room to clean up
    // without the caller having to pass it in.
    private readonly ConcurrentDictionary<string, string> _connectionMeetingIds = new();

    public IReadOnlyCollection<CallParticipant> AddParticipant(string meetingId, string connectionId, string participantId, string name)
    {
        var room = _rooms.GetOrAdd(meetingId, _ => new ConcurrentDictionary<string, CallParticipant>());

        // Snapshot taken BEFORE adding this connection, so the new joiner doesn't see
        // itself in its own "who's already here" list.
        var existingParticipants = room.Values.ToList();

        room[connectionId] = new CallParticipant { ConnectionId = connectionId, ParticipantId = participantId, Name = name };
        _connectionMeetingIds[connectionId] = meetingId;

        return existingParticipants;
    }

    public (string? MeetingId, CallParticipant? Participant) RemoveParticipant(string connectionId)
    {
        if (!_connectionMeetingIds.TryRemove(connectionId, out var meetingId))
            return (null, null);

        CallParticipant? removed = null;
        if (_rooms.TryGetValue(meetingId, out var room))
        {
            room.TryRemove(connectionId, out removed);
            if (room.IsEmpty) _rooms.TryRemove(meetingId, out _);
        }

        return (meetingId, removed);
    }

    public IEnumerable<string> GetConnectionIdsForParticipant(string meetingId, string participantExternalId) =>
        _rooms.TryGetValue(meetingId, out var room)
            ? room.Values.Where(p => p.ParticipantId == participantExternalId).Select(p => p.ConnectionId).ToList()
            : Enumerable.Empty<string>();
}
