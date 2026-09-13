using System.ComponentModel.DataAnnotations;

namespace Coon.Meeting.Api.Models.Dtos;

/// <summary>
/// Only the fields an edit form would send. Attendees are managed separately via
/// AttendeesController, so this never touches Meeting.Attendees.
/// </summary>
public class UpdateMeetingDto
{
    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public DateTime ScheduledAt { get; set; }

    public string? TimeZone { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Location { get; set; }
    public string? MeetingLink { get; set; }
}
