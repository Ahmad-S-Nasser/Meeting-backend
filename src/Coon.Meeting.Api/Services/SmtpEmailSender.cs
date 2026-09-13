using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Coon.Meeting.Api.Config;

namespace Coon.Meeting.Api.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpClient _smtpClient;
    private readonly string _fromAddress;
    private readonly IIcsBuilder _ics;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(SmtpSettings settings, IIcsBuilder ics, ILogger<SmtpEmailSender> logger)
    {
        _fromAddress = settings.FromAddress;
        _ics = ics;
        _logger = logger;

        _smtpClient = new SmtpClient(settings.Host, settings.Port)
        {
            Credentials = new NetworkCredential(settings.Username, settings.Password),
            EnableSsl = true,
            // SmtpClient's default is 100 seconds - since this fires synchronously right after
            // a write, a slow or unreachable mail server would otherwise make every meeting
            // create/update/cancel hang for up to that long instead of failing fast.
            Timeout = 10_000,
        };
    }

    public async Task SendMeetingInviteAsync(Models.Meeting meeting, string icsMethod, int sequence)
    {
        var recipients = (meeting.Attendees ?? new List<Models.MeetingAttendee>())
            .Select(a => a?.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recipients.Count == 0) return;

        var icsContent = _ics.Build(meeting, icsMethod, sequence);
        var subject = icsMethod == IcsMethods.Cancel ? $"Cancelled: {meeting.Title}" : $"Invitation: {meeting.Title}";

        try
        {
            foreach (var recipient in recipients)
            {
                var mail = new MailMessage
                {
                    From = new MailAddress(_fromAddress),
                    Subject = subject,
                    IsBodyHtml = true,
                };
                mail.To.Add(recipient!);

                // A calendar invite is an HTML mail with a text/calendar ALTERNATE VIEW, not a
                // plain attachment - mail clients look for that content type to offer
                // Accept/Decline. The same bytes as only an attachment show up as a file to
                // download, which is the difference between an invite and an email about one.
                mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(BuildBody(meeting, icsMethod), null, "text/html"));

                var calendarType = new ContentType("text/calendar");
                calendarType.Parameters.Add("method", icsMethod);
                calendarType.Parameters.Add("name", "invite.ics");

                var calendarView = AlternateView.CreateAlternateViewFromString(icsContent, calendarType);
                calendarView.TransferEncoding = TransferEncoding.SevenBit;
                mail.AlternateViews.Add(calendarView);

                // Outlook in particular is happier when the file is also present by name.
                mail.Attachments.Add(Attachment.CreateAttachmentFromString(icsContent, "invite.ics", Encoding.UTF8, "text/calendar"));

                await _smtpClient.SendMailAsync(mail);
            }
        }
        catch (Exception ex)
        {
            // Never let invite delivery take down the write that already succeeded.
            _logger.LogWarning(ex, "Failed to email meeting invite for meeting {MeetingId}.", meeting.Id);
        }
    }

    private static string BuildBody(Models.Meeting meeting, string icsMethod)
    {
        var when = $"{meeting.ScheduledAt:f} UTC";
        var heading = icsMethod == IcsMethods.Cancel ? "Meeting cancelled" : "You're invited";
        var linkLine = !string.IsNullOrWhiteSpace(meeting.MeetingLink)
            ? $"<p style=\"margin:8px 0 0 0;\"><a href=\"{meeting.MeetingLink}\">{meeting.MeetingLink}</a></p>"
            : string.Empty;

        return $@"
            <div style=""font-family: -apple-system, Segoe UI, Roboto, sans-serif; max-width: 480px;"">
                <h2 style=""color:#0f172a; margin-top:0; font-size:20px;"">{heading}</h2>
                <div style=""background:#f1f5f9; border-left:4px solid #14b8a6; padding:16px; margin:24px 0;"">
                    <p style=""margin:0; font-weight:600; color:#0f172a;"">{meeting.Title}</p>
                    <p style=""margin:4px 0 0 0; font-size:14px; color:#64748b;"">{when}</p>
                    {linkLine}
                </div>
            </div>";
    }
}
