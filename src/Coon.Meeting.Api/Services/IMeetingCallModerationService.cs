namespace Coon.Meeting.Api.Services;

/// <summary>
/// The REST-triggered "force this participant out of a live call" path - separate from
/// IMeetingAccessService, which only decides whether a *future* join/mint is allowed.
/// </summary>
public interface IMeetingCallModerationService
{
    /// <summary>
    /// Pushes eventName ("Kicked" or "Blocked") to every live connection for this participant
    /// in this meeting, then removes each from the room/group and broadcasts their departure -
    /// the same teardown any other participant's own leave/disconnect already triggers.
    /// Returns how many live connections were found and force-removed (0 if they weren't
    /// actually in the call).
    /// </summary>
    Task<int> ForceDisconnectAsync(string meetingId, string participantExternalId, string eventName, string? reason);

    /// <summary>
    /// Pushes a live "CapabilityChanged" event ({ capability, allowed }) to every live
    /// connection for this participant in this meeting - unlike ForceDisconnectAsync, this
    /// never touches the registry or group membership, so the participant stays in the call.
    /// Also records the grant so a later reconnect (MeetingCallHub.JoinCall) can echo it back,
    /// since the registry itself only tracks live connections, not what was granted. A no-op,
    /// not an error, if the participant has no live connection right now - the REST call still
    /// succeeds, there's just nothing to push to yet.
    /// </summary>
    Task PushCapabilityChangeAsync(string meetingId, string participantExternalId, string capability, bool allowed);
}
