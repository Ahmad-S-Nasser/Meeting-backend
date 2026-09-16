using Coon.Meeting.Api.Hubs;
using Coon.Meeting.Api.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Coon.Meeting.Api.Services;

public class MeetingCallModerationService : IMeetingCallModerationService
{
    private readonly IMeetingRoomRegistry _registry;
    private readonly IHubContext<MeetingCallHub> _hubContext;

    public MeetingCallModerationService(IMeetingRoomRegistry registry, IHubContext<MeetingCallHub> hubContext)
    {
        _registry = registry;
        _hubContext = hubContext;
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
}
