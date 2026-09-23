using Coon.Meeting.Api.Hubs;
using Coon.Meeting.Api.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Coon.Meeting.Api.Services;

public class MeetingCallModerationService : IMeetingCallModerationService
{
    private readonly IMeetingRoomRegistry _registry;
    private readonly IHubContext<MeetingCallHub> _hubContext;
    private readonly ICallCapabilityGrantStore _grants;

    public MeetingCallModerationService(IMeetingRoomRegistry registry, IHubContext<MeetingCallHub> hubContext, ICallCapabilityGrantStore grants)
    {
        _registry = registry;
        _hubContext = hubContext;
        _grants = grants;
    }

    public async Task<int> ForceDisconnectAsync(string meetingId, string participantExternalId, string eventName, string? reason)
    {
        var connectionIds = _registry.GetConnectionIdsForParticipant(meetingId, participantExternalId).ToList();

        foreach (var connectionId in connectionIds)
        {
            // There's no supported way to sever a transport connection by id from outside a
            // Hub instance - this pushes the event and stops relaying to them (removed from
            // the group), then relies on the SDK's own client code (ours) to call
            // disconnect() on receipt. Every OTHER participant's existing
            // onParticipantLeft -> closePeer() (unchanged) closes its own RTCPeerConnection to
            // this connection the moment the broadcast below fires, which is what actually
            // cuts off media in both directions in practice.
            await _hubContext.Clients.Client(connectionId).SendAsync(eventName, new { reason });

            _registry.RemoveParticipant(connectionId);
            await MeetingCallBroadcastHelper.RemoveAndBroadcastLeaveAsync(_hubContext.Groups, _hubContext.Clients, connectionId, meetingId);
        }

        return connectionIds.Count;
    }

    public async Task PushCapabilityChangeAsync(string meetingId, string participantExternalId, string capability, bool allowed)
    {
        // Recorded regardless of whether they're currently connected, so a reconnect
        // (MeetingCallHub.JoinCall) can echo the last-known grant back to them.
        _grants.SetGrant(meetingId, participantExternalId, capability, allowed);

        var connectionIds = _registry.GetConnectionIdsForParticipant(meetingId, participantExternalId).ToList();

        foreach (var connectionId in connectionIds)
        {
            // Unlike ForceDisconnectAsync: no registry/group cleanup here - this participant
            // isn't going anywhere, only their client-side capability enforcement changes.
            await _hubContext.Clients.Client(connectionId).SendAsync("CapabilityChanged", new { capability, allowed });
        }
    }
}
