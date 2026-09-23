using Coon.Meeting.Api.Auth;
using Coon.Meeting.Api.Helpers;
using Coon.Meeting.Api.Models;
using Coon.Meeting.Api.Realtime;
using Coon.Meeting.Api.Repositories;
using Coon.Meeting.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Coon.Meeting.Api.Hubs;

/// <summary>
/// WebRTC signaling for a meeting's call. Relays SDP offers/answers and ICE candidates between
/// participants - audio/video never touches this hub, it flows peer-to-peer (or via TURN) once
/// negotiation completes. The room is the meeting's own id.
/// </summary>
[Authorize(AuthenticationSchemes = ParticipantTokenScheme.SchemeName)]
public class MeetingCallHub : Hub
{
    private readonly IMeetingRepository _meetings;
    private readonly IWebhookDispatcher _webhooks;
    private readonly IMeetingRoomRegistry _registry;
    private readonly IMeetingAccessService _access;
    private readonly ICallCapabilityGrantStore _grants;

    public MeetingCallHub(IMeetingRepository meetings, IWebhookDispatcher webhooks, IMeetingRoomRegistry registry, IMeetingAccessService access, ICallCapabilityGrantStore grants)
    {
        _meetings = meetings;
        _webhooks = webhooks;
        _registry = registry;
        _access = access;
        _grants = grants;
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

        // Re-checked here, not just at token-mint time: a token minted before a block took
        // effect (or before an attendee was removed) is still a technically-valid JWT - this
        // is what actually keeps a blocked/uninvited id out of the room, not just out of a
        // fresh mint.
        var access = _access.CheckAccess(meeting, participantId);
        if (access != MeetingAccessResult.Allowed)
        {
            await Clients.Caller.SendAsync("AccessDenied", new { reason = access == MeetingAccessResult.Blocked ? "blocked" : "not_invited" });
            return;
        }

        var existingParticipants = _registry.AddParticipant(meetingId, Context.ConnectionId, participantId, name);
        await Groups.AddToGroupAsync(Context.ConnectionId, meetingId);

        // Convention: the new joiner always initiates the WebRTC offer to each existing member,
        // never the reverse - avoids a double-offer glare condition between two peers
        // negotiating at once, with no tie-breaker needed.
        await Clients.Caller.SendAsync("ExistingParticipants", existingParticipants);
        await Clients.OthersInGroup(meetingId).SendAsync("ParticipantJoined", Context.ConnectionId, participantId, name);

        // Echoes any live-call capability grant this participant already had before this
        // connection existed (a fresh tab, a reconnect after a drop, ...) - the grant itself
        // lives in ICallCapabilityGrantStore, not the room registry, precisely so it survives
        // this. Same "CapabilityChanged" event a live push uses, just replayed on join.
        foreach (var (capability, allowed) in _grants.GetGrants(meetingId, participantId))
        {
            await Clients.Caller.SendAsync("CapabilityChanged", new { capability, allowed });
        }

        await _webhooks.DispatchAsync(meeting.TenantId, WebhookEventTypes.ParticipantJoined, new
        {
            meetingId,
            connectionId = Context.ConnectionId,
            participantId,
            name,
        });
    }

    public async Task LeaveCall(string meetingId)
    {
        var (_, self) = _registry.RemoveParticipant(Context.ConnectionId);
        await MeetingCallBroadcastHelper.RemoveAndBroadcastLeaveAsync(Groups, Clients, Context.ConnectionId, meetingId);
        await DispatchParticipantLeftAsync(meetingId, self);
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

    /// <summary>
    /// Pure liveness broadcast, same shape as UpdateMediaState - tells the room "my next
    /// renegotiated video track is a screen share, not a second camera." The actual screen
    /// track itself is added to the existing per-peer RTCPeerConnection and renegotiated over
    /// the SendOffer/SendAnswer path already above; nothing about that path needed to change.
    /// </summary>
    public async Task UpdateScreenShareState(string meetingId, bool isSharing)
    {
        if (!ClaimMatchesRoom(meetingId)) throw new HubException("Not authorized for this meeting.");
        await Clients.OthersInGroup(meetingId).SendAsync("ScreenShareStateChanged", Context.ConnectionId, isSharing);
    }

    /// <summary>
    /// Call-scoped and ephemeral by design, same trust model as every other broadcast here - no
    /// registry/DB write, so there is no history endpoint and a reconnecting or late-joining
    /// participant sees nothing sent before they arrived.
    /// </summary>
    public async Task SendChatMessage(string meetingId, string text)
    {
        if (!ClaimMatchesRoom(meetingId)) throw new HubException("Not authorized for this meeting.");
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > 2000) throw new HubException("Message too long.");

        var participantId = TenantContext.ParticipantId(Context.User!);
        var name = Context.User!.FindFirst("name")?.Value ?? "Participant";
        await Clients.OthersInGroup(meetingId).SendAsync("ReceiveChatMessage", Context.ConnectionId, participantId, name, text, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Same pure-liveness-broadcast shape as UpdateMediaState/UpdateScreenShareState - this is
    /// the actual consent notice a recording is required to give every participant, not just a
    /// UI nicety. Recording itself is entirely client-side (composited by whichever participant
    /// started it); Coon.Meeting never touches the recorded media and has no storage for it.
    /// </summary>
    public async Task UpdateRecordingState(string meetingId, bool isRecording)
    {
        if (!ClaimMatchesRoom(meetingId)) throw new HubException("Not authorized for this meeting.");
        await Clients.OthersInGroup(meetingId).SendAsync("RecordingStateChanged", Context.ConnectionId, isRecording);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // A dropped tab must still tell the room to tear down that one peer connection, or the
        // other participants are left staring at a frozen video tile.
        var (meetingId, self) = _registry.RemoveParticipant(Context.ConnectionId);
        if (meetingId != null)
        {
            await MeetingCallBroadcastHelper.RemoveAndBroadcastLeaveAsync(Groups, Clients, Context.ConnectionId, meetingId);
            await DispatchParticipantLeftAsync(meetingId, self);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Task DispatchParticipantLeftAsync(string meetingId, CallParticipant? self)
    {
        // The tenant comes straight off this connection's own claim - no DB round trip needed,
        // matching every other method here except JoinCall.
        var tenantId = TenantContext.TenantId(Context.User!);
        return _webhooks.DispatchAsync(tenantId, WebhookEventTypes.ParticipantLeft, new
        {
            meetingId,
            connectionId = Context.ConnectionId,
            participantId = self?.ParticipantId,
            name = self?.Name,
        });
    }
}
