using Coon.Meeting.Api.Models;

namespace Coon.Meeting.Api.Services;

public class MeetingAccessService : IMeetingAccessService
{
    public MeetingAccessResult CheckAccess(Models.Meeting meeting, string participantExternalId)
    {
        // Checked first, independent of Visibility - a blocked id must stay blocked even on
        // an Any meeting, since "kick the troll off the public link" is exactly why Block
        // exists.
        if (meeting.BlockedParticipantIds.Contains(participantExternalId, StringComparer.Ordinal))
            return MeetingAccessResult.Blocked;

        if (meeting.Visibility == MeetingVisibility.Any)
            return MeetingAccessResult.Allowed;

        if (string.Equals(meeting.CreatedByExternalId, participantExternalId, StringComparison.Ordinal))
            return MeetingAccessResult.Allowed;

        if (meeting.Attendees.Any(a => string.Equals(a.ExternalParticipantId, participantExternalId, StringComparison.Ordinal)))
            return MeetingAccessResult.Allowed;

        return MeetingAccessResult.NotInvited;
    }
}
