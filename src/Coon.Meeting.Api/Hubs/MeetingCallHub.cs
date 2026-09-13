using System.Collections.Concurrent;
using Coon.Meeting.Api.Auth;
using Coon.Meeting.Api.Helpers;
using Coon.Meeting.Api.Models;
using Coon.Meeting.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Coon.Meeting.Api.Hubs;

/// <summary>One connection's identity within a call room, as sent to a newly-joining peer.</summary>
public class CallParticipant
{
    public string ConnectionId { get; set; } = string.Empty;
    public string ParticipantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// WebRTC signaling for a meeting's call. Relays SDP offers/answers and ICE candidates between
/// participants - audio/video never touches this hub, it flows peer-to-peer (or via TURN) once
/// negotiation completes. The room is the meeting's own id.
/// </summary>
[Authorize(AuthenticationSchemes = ParticipantTokenScheme.SchemeName)]
public class MeetingCallHub : Hub
{
    private readonly IMeetingRepository _meetings;

    // SignalR groups don't expose their own membership, so call rooms are tracked here:
    // meetingId -> (connectionId -> participant). Needed both to hand a joining peer the list
    // of who's already in the room, and to notify the room on a dropped connection
    // (OnDisconnectedAsync) without the client having called LeaveCall first.
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CallParticipant>> Rooms = new();

    // connectionId -> meetingId, so OnDisconnectedAsync knows which room to clean up without
    // the caller having to pass it in (a disconnect carries no arguments).
    private static readonly ConcurrentDictionary<string, string> ConnectionMeetingIds = new();

    public MeetingCallHub(IMeetingRepository meetings)
    {
        _meetings = meetings;
    }

    /// <summary>
    /// Cheap, per-method check: does the token's own meetingId claim match the room the caller
    /// is trying to act on. No DB round trip - that only happens once, in JoinCall.
    /// </summary>
    private bool ClaimMatchesRoom(string meetingId) =>
        !string.IsNullOrEmpty(meetingId) && TenantContext.MeetingId(Context.User!) == meetingId;

    public async Task JoinCall(string meetingId)
    {
        if (!ClaimMatchesRoom(meetingId))
            throw new HubException("Not authorized for this meeting.");

        // One DB round trip per connection, here only: confirms the meeting still exists for
        // this tenant and hasn't been cancelled since the token was minted. Every other method
        // trusts the claim alone - re-hitting the DB per ICE candidate would be wasteful for a
        // check that can't meaningfully change mid-call.
        var meeting = await _meetings.GetByIdAsync(TenantContext.TenantId(Context.User!), meetingId);
        if (meeting == null || meeting.Status == MeetingStatus.Cancelled)
            throw new HubException("This meeting is no longer available.");

        var participantId = TenantContext.ParticipantId(Context.User!);
        var name = Context.User!.FindFirst("name")?.Value ?? "Participant";
        var self = new CallParticipant { ConnectionId = Context.ConnectionId, ParticipantId = participantId, Name = name };

        var room = Rooms.GetOrAdd(meetingId, _ => new ConcurrentDictionary<string, CallParticipant>());

        // Snapshot taken BEFORE adding this connection, so the new joiner doesn't see itself in
        // its own "who's already here" list.
        var existingParticipants = room.Values.ToList();

        room[Context.ConnectionId] = self;
        ConnectionMeetingIds[Context.ConnectionId] = meetingId;
        await Groups.AddToGroupAsync(Context.ConnectionId, meetingId);

        // Convention: the new joiner always initiates the WebRTC offer to each existing member,
        // never the reverse - avoids a double-offer glare condition between two peers
        // negotiating at once, with no tie-breaker needed.
        await Clients.Caller.SendAsync("ExistingParticipants", existingParticipants);
        await Clients.OthersInGroup(meetingId).SendAsync("ParticipantJoined", self.ConnectionId, self.ParticipantId, self.Name);
    }

    public async Task LeaveCall(string meetingId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, meetingId);
        RemoveFromRoom(meetingId);

        await Clients.Group(meetingId).SendAsync("ParticipantLeft", Context.ConnectionId);
    }

    public async Task SendOffer(string meetingId, string toConnectionId, string sdp)
    {
        if (!ClaimMatchesRoom(meetingId)) throw new HubException("Not authorized for this meeting.");
        await Clients.Client(toConnectionId).SendAsync("ReceiveOffer", Context.ConnectionId, sdp);
    }

    public async Task SendAnswer(string meetingId, string toConnectionId, string sdp)
    {
        if (!ClaimMatchesRoom(meetingId)) throw new HubException("Not authorized for this meeting.");
        await Clients.Client(toConnectionId).SendAsync("ReceiveAnswer", Context.ConnectionId, sdp);
    }

    public async Task SendIceCandidate(string meetingId, string toConnectionId, string candidate)
    {
        if (!ClaimMatchesRoom(meetingId)) throw new HubException("Not authorized for this meeting.");
        await Clients.Client(toConnectionId).SendAsync("ReceiveIceCandidate", Context.ConnectionId, candidate);
    }

    public async Task UpdateMediaState(string meetingId, bool micOn, bool cameraOn)
    {
        if (!ClaimMatchesRoom(meetingId)) throw new HubException("Not authorized for this meeting.");
        await Clients.OthersInGroup(meetingId).SendAsync("MediaStateChanged", Context.ConnectionId, micOn, cameraOn);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // A dropped tab must still tell the room to tear down that one peer connection, or the
        // other participants are left staring at a frozen video tile.
        if (ConnectionMeetingIds.TryGetValue(Context.ConnectionId, out var meetingId))
        {
            RemoveFromRoom(meetingId);
            await Clients.Group(meetingId).SendAsync("ParticipantLeft", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private void RemoveFromRoom(string meetingId)
    {
        ConnectionMeetingIds.TryRemove(Context.ConnectionId, out _);

        if (Rooms.TryGetValue(meetingId, out var room))
        {
            room.TryRemove(Context.ConnectionId, out _);
            if (room.IsEmpty) Rooms.TryRemove(meetingId, out _);
        }
    }
}
