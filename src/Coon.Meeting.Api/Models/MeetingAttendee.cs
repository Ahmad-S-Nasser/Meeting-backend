namespace Coon.Meeting.Api.Models;

/// <summary>
/// An opaque participant tuple supplied directly by the integrator - there is no contributor
/// directory here to resolve ids against, unlike the SquadSpace reference implementation.
/// </summary>
public class MeetingAttendee
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ExternalParticipantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
}
