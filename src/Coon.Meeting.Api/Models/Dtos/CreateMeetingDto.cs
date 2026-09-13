using System.ComponentModel.DataAnnotations;

namespace Coon.Meeting.Api.Models.Dtos;

public class CreateMeetingDto
{
    public string? ExternalRef { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public DateTime ScheduledAt { get; set; }

    public string? TimeZone { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Location { get; set; }
    public string? MeetingLink { get; set; }

    [Required]
    public AttendeeDto Organizer { get; set; } = new();

    public List<AttendeeDto> Attendees { get; set; } = new();
}
