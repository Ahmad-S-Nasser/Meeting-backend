namespace Coon.Meeting.Api.Services;

public interface IEmailSender
{
    /// <summary>
    /// Emails a calendar invite (or cancellation) to every attendee with an address. Best-effort
    /// by design - a meeting that was saved must not fail because SMTP is down or one address
    /// bounces.
    /// </summary>
    Task SendMeetingInviteAsync(Models.Meeting meeting, string icsMethod, int sequence);
}
