namespace Coon.Meeting.Api.Models;

public enum MeetingStatus
{
    Scheduled,
    Cancelled,
}

public class Meeting
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The integrator's own id for this meeting, if it wants to correlate one. Opaque to us.</summary>
    public string? ExternalRef { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Start time, stored in UTC.</summary>
    public DateTime ScheduledAt { get; set; }

    /// <summary>IANA time zone the meeting was scheduled in - "Africa/Cairo", "Europe/London".</summary>
    public string? TimeZone { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Location { get; set; }
    public string? MeetingLink { get; set; }

    public string CreatedByExternalId { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public string? CreatedByEmail { get; set; }

    public MeetingStatus Status { get; set; } = MeetingStatus.Scheduled;

    public MeetingVisibility Visibility { get; set; } = MeetingVisibility.Private;

    /// <summary>External ids force-disconnected and permanently barred from rejoining this
    /// meeting - checked by IMeetingAccessService regardless of Visibility.</summary>
    public List<string> BlockedParticipantIds { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the "starting soon" reminder webhook was sent, or null if it hasn't been yet.</summary>
    public DateTime? ReminderSentAt { get; set; }

    public List<MeetingAttendee> Attendees { get; set; } = new();
}
