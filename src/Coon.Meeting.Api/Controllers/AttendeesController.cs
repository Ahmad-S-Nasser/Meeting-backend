using Coon.Meeting.Api.Auth;
using Coon.Meeting.Api.Helpers;
using Coon.Meeting.Api.Models;
using Coon.Meeting.Api.Models.Dtos;
using Coon.Meeting.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coon.Meeting.Api.Controllers;

[ApiController]
[Route("api/v1/meetings/{meetingId}/attendees")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationSchemeOptions.SchemeName)]
public class AttendeesController : ControllerBase
{
    private readonly IMeetingRepository _meetings;

    public AttendeesController(IMeetingRepository meetings)
    {
        _meetings = meetings;
    }

    // GET /api/v1/meetings/{meetingId}/attendees
    [HttpGet]
    public async Task<ActionResult<List<MeetingAttendee>>> List(string meetingId)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId);
        if (meeting == null) return NotFound();
        return Ok(meeting.Attendees);
    }

    // POST /api/v1/meetings/{meetingId}/attendees
    [HttpPost]
    public async Task<ActionResult<MeetingAttendee>> Add(string meetingId, [FromBody] AttendeeDto dto)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId);
        if (meeting == null) return NotFound();

        var attendee = new MeetingAttendee
        {
            ExternalParticipantId = dto.ExternalId,
            Name = dto.Name,
            Email = dto.Email,
        };

        meeting.Attendees.Add(attendee);
        meeting.UpdatedAt = DateTime.UtcNow;
        await _meetings.UpdateAsync(meeting);

        return Ok(attendee);
    }

    // DELETE /api/v1/meetings/{meetingId}/attendees/{attendeeId}
    [HttpDelete("{attendeeId}")]
    public async Task<IActionResult> Remove(string meetingId, string attendeeId)
    {
        var meeting = await GetOwnedMeetingAsync(meetingId);
        if (meeting == null) return NotFound();

        var attendee = meeting.Attendees.FirstOrDefault(a => a.Id == attendeeId);
        if (attendee == null) return NotFound();

        meeting.Attendees.Remove(attendee);
        meeting.UpdatedAt = DateTime.UtcNow;
        await _meetings.UpdateAsync(meeting);

        return NoContent();
    }

    private Task<Models.Meeting?> GetOwnedMeetingAsync(string meetingId) =>
        _meetings.GetByIdAsync(TenantContext.TenantId(User), meetingId);
}
