using Coon.Meeting.Api.Models;

namespace Coon.Meeting.Api.Services;

public enum MeetingAccessResult
{
    Allowed,
    Blocked,
    NotInvited,
}

/// <summary>
/// The single source of truth for "may this external id join this meeting" - shared by
/// ParticipantTokensController.Mint and MeetingCallHub.JoinCall, so a block or an attendee
/// change takes effect immediately rather than only at the next token mint.
/// </summary>
public interface IMeetingAccessService
{
    // "Models.Meeting" fully qualified - the Coon.Meeting.Api root namespace collides with
    // the bare "Meeting" type name (CS0118), same trap already documented elsewhere in
    // this codebase (and in the Dashboard's own HANDOFF.md).
    MeetingAccessResult CheckAccess(Models.Meeting meeting, string participantExternalId);
}
