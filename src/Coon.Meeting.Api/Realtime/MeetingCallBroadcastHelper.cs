using Microsoft.AspNetCore.SignalR;

namespace Coon.Meeting.Api.Realtime;

/// <summary>
/// The one piece of "leave the room and tell everyone" logic that must be identical whether
/// triggered by the hub itself (LeaveCall/OnDisconnectedAsync) or by a REST-initiated
/// kick/block reaching in from outside - IGroupManager/IHubClients are implemented by both
/// a Hub instance's own Groups/Clients and IHubContext&lt;T&gt;'s, so this one method works from
/// either caller without duplicating the logic.
/// </summary>
public static class MeetingCallBroadcastHelper
{
    // Hub.Clients is IHubCallerClients and IHubContext<T>.Clients is IHubClients - two
    // separate interfaces that neither inherits from the other, only sharing the common
    // generic base IHubClients<IClientProxy> - hence that common base here, not either
    // concrete type, so both a Hub instance and an IHubContext<T> can call this.
    public static async Task RemoveAndBroadcastLeaveAsync(
        IGroupManager groups, IHubClients<IClientProxy> clients,
        string connectionId, string meetingId)
    {
        await groups.RemoveFromGroupAsync(connectionId, meetingId);
        await clients.Group(meetingId).SendAsync("ParticipantLeft", connectionId);
    }
}
