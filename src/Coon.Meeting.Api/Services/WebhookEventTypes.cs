namespace Coon.Meeting.Api.Services;

public static class WebhookEventTypes
{
    public const string MeetingCreated = "meeting.created";
    public const string MeetingUpdated = "meeting.updated";
    public const string MeetingCancelled = "meeting.cancelled";
    public const string MeetingReminder = "meeting.reminder";
    public const string ParticipantJoined = "participant.joined";
    public const string ParticipantLeft = "participant.left";
}
