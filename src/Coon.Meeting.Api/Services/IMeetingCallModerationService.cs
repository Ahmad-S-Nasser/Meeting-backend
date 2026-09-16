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
}
