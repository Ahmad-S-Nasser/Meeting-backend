using Coon.Meeting.Api.Auth;
using Coon.Meeting.Api.Helpers;
using Coon.Meeting.Api.Repositories;
using Coon.Meeting.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coon.Meeting.Api.Controllers;

[ApiController]
[Route("api/v1/meetings/{id}/calendar.ics")]
[Authorize(AuthenticationSchemes = $"{ApiKeyAuthenticationSchemeOptions.SchemeName},{ParticipantTokenScheme.SchemeName}")]
public class CalendarController : ControllerBase
{
    private readonly IMeetingRepository _meetings;
    private readonly IIcsBuilder _ics;

    public CalendarController(IMeetingRepository meetings, IIcsBuilder ics)
    {
        _meetings = meetings;
        _ics = ics;
    }

    /// <summary>
    /// Downloads the meeting as an .ics file. PUBLISH rather than REQUEST: a file someone
    /// downloaded is a copy for their own calendar, not an invitation to reply to.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Download(string id)
    {
        // A participant token is scoped to one meeting; an ApiKey has no meetingId claim at all
        // and is trusted for any meeting in its own tenant.
        var tokenMeetingId = User.FindFirst("meetingId")?.Value;
        if (tokenMeetingId != null && tokenMeetingId != id) return Forbid();

        var meeting = await _meetings.GetByIdAsync(TenantContext.TenantId(User), id);
        if (meeting == null) return NotFound();

        var ics = _ics.Build(meeting, IcsMethods.Publish, IcsSequence.For(meeting));
        var bytes = System.Text.Encoding.UTF8.GetBytes(ics);

        return File(bytes, "text/calendar", $"{Slug(meeting.Title)}.ics");
    }

    private static string Slug(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "meeting";

        var cleaned = new string(value
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray());

        return cleaned.Trim('-').Replace("--", "-") is { Length: > 0 } s ? s : "meeting";
    }
}
