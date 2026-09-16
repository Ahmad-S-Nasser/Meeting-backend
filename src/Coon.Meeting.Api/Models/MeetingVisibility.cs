namespace Coon.Meeting.Api.Models;

/// <summary>
/// Private: only the organizer or a listed attendee may join - enforced by
/// IMeetingAccessService. Any: literally anyone holding a link can join, no attendee-list
/// check at all - the integrator's own frontend is responsible for handing out that link.
/// </summary>
public enum MeetingVisibility
{
    Private,
    Any,
}
