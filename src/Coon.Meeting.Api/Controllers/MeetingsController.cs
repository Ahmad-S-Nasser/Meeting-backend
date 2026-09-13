using Coon.Meeting.Api.Auth;
using Coon.Meeting.Api.Helpers;
using Coon.Meeting.Api.Models;
using Coon.Meeting.Api.Models.Dtos;
using Coon.Meeting.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coon.Meeting.Api.Controllers;

[ApiController]
[Route("api/v1/meetings")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationSchemeOptions.SchemeName)]
public class MeetingsController : ControllerBase
{
    private readonly IMeetingRepository _meetings;

    public MeetingsController(IMeetingRepository meetings)
    {
        _meetings = meetings;
    }

    // GET /api/v1/meetings
    [HttpGet]
    public async Task<ActionResult<List<Models.Meeting>>> List()
    {
        var tenantId = TenantContext.TenantId(User);
        var meetings = await _meetings.GetByTenantIdAsync(tenantId);
        return Ok(meetings);
    }

    // GET /api/v1/meetings/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<Models.Meeting>> GetById(string id)
    {
        var tenantId = TenantContext.TenantId(User);
        var meeting = await _meetings.GetByIdAsync(tenantId, id);
        if (meeting == null) return NotFound();
        return Ok(meeting);
    }

    // POST /api/v1/meetings
    [HttpPost]
    public async Task<ActionResult<Models.Meeting>> Create([FromBody] CreateMeetingDto dto)
    {
        var tenantId = TenantContext.TenantId(User);

        var meeting = new Models.Meeting
        {
            TenantId = tenantId,
            ExternalRef = dto.ExternalRef,
            Title = dto.Title,
            Description = dto.Description,
            ScheduledAt = dto.ScheduledAt,
            TimeZone = dto.TimeZone,
            DurationMinutes = dto.DurationMinutes,
            Location = dto.Location,
            MeetingLink = dto.MeetingLink,
            CreatedByExternalId = dto.Organizer.ExternalId,
            CreatedByName = dto.Organizer.Name,
            CreatedByEmail = dto.Organizer.Email,
            Attendees = dto.Attendees.Select(a => new MeetingAttendee
            {
                ExternalParticipantId = a.ExternalId,
                Name = a.Name,
                Email = a.Email,
            }).ToList(),
        };

        await _meetings.CreateAsync(meeting);

        return CreatedAtAction(nameof(GetById), new { id = meeting.Id }, meeting);
    }

    // PUT /api/v1/meetings/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateMeetingDto dto)
    {
        var tenantId = TenantContext.TenantId(User);
        var existing = await _meetings.GetByIdAsync(tenantId, id);
        if (existing == null) return NotFound();

        existing.Title = dto.Title;
        existing.Description = dto.Description;
        existing.ScheduledAt = dto.ScheduledAt;
        existing.TimeZone = dto.TimeZone;
        existing.DurationMinutes = dto.DurationMinutes;
        existing.Location = dto.Location;
        existing.MeetingLink = dto.MeetingLink;
        existing.UpdatedAt = DateTime.UtcNow;

        // A time change means the "starting soon" reminder needs to fire again for the new time.
        if (existing.ScheduledAt != dto.ScheduledAt)
            existing.ReminderSentAt = null;

        await _meetings.UpdateAsync(existing);
        return NoContent();
    }

    // DELETE /api/v1/meetings/{id} - soft-cancel, not a hard delete: an external system
    // building workflows against this service should be able to ask "what happened to this
    // meeting" after the fact.
    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(string id)
    {
        var tenantId = TenantContext.TenantId(User);
        var meeting = await _meetings.GetByIdAsync(tenantId, id);
        if (meeting == null) return NotFound();

        meeting.Status = MeetingStatus.Cancelled;
        meeting.UpdatedAt = DateTime.UtcNow;
        await _meetings.UpdateAsync(meeting);

        return NoContent();
    }
}
