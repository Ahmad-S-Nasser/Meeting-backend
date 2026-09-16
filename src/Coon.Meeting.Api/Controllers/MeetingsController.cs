using Coon.Meeting.Api.Auth;
using Coon.Meeting.Api.Helpers;
using Coon.Meeting.Api.Models;
using Coon.Meeting.Api.Models.Dtos;
using Coon.Meeting.Api.Repositories;
using Coon.Meeting.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coon.Meeting.Api.Controllers;

[ApiController]
[Route("api/v1/meetings")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationSchemeOptions.SchemeName)]
public class MeetingsController : ControllerBase
{
    private readonly IMeetingRepository _meetings;
    private readonly IWebhookDispatcher _webhooks;
    private readonly IEmailSender _emailSender;

    public MeetingsController(IMeetingRepository meetings, IWebhookDispatcher webhooks, IEmailSender emailSender)
    {
        _meetings = meetings;
        _webhooks = webhooks;
        _emailSender = emailSender;
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
            Visibility = dto.Visibility ?? MeetingVisibility.Private,
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

        await _webhooks.DispatchAsync(tenantId, WebhookEventTypes.MeetingCreated, meeting);
        await _emailSender.SendMeetingInviteAsync(meeting, IcsMethods.Request, sequence: 0);

        return CreatedAtAction(nameof(GetById), new { id = meeting.Id }, meeting);
    }

    // PUT /api/v1/meetings/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateMeetingDto dto)
    {
        var tenantId = TenantContext.TenantId(User);
        var existing = await _meetings.GetByIdAsync(tenantId, id);
        if (existing == null) return NotFound();

        var wasScheduledAt = existing.ScheduledAt;
        var wasDuration = existing.DurationMinutes;
        var wasTitle = existing.Title;
        var wasLocation = existing.Location;
        var wasMeetingLink = existing.MeetingLink;
        var wasTimeZone = existing.TimeZone;

        existing.Title = dto.Title;
        existing.Description = dto.Description;
        existing.ScheduledAt = dto.ScheduledAt;
        existing.TimeZone = dto.TimeZone;
        existing.DurationMinutes = dto.DurationMinutes;
        existing.Location = dto.Location;
        existing.MeetingLink = dto.MeetingLink;
        if (dto.Visibility.HasValue) existing.Visibility = dto.Visibility.Value;
        existing.UpdatedAt = DateTime.UtcNow;

        // A time change means the "starting soon" reminder needs to fire again for the new time.
        if (existing.ScheduledAt != wasScheduledAt)
            existing.ReminderSentAt = null;

        await _meetings.UpdateAsync(existing);

        await _webhooks.DispatchAsync(tenantId, WebhookEventTypes.MeetingUpdated, existing);

        // Only re-invite when something a calendar entry actually shows has moved - re-sending
        // on every save trains people to ignore the invites. Same UID, higher SEQUENCE: clients
        // update the existing entry rather than adding a second one.
        var rescheduled = wasScheduledAt != existing.ScheduledAt
                          || wasDuration != existing.DurationMinutes
                          || !string.Equals(wasTitle, existing.Title, StringComparison.Ordinal)
                          || !string.Equals(wasLocation, existing.Location, StringComparison.Ordinal)
                          || !string.Equals(wasMeetingLink, existing.MeetingLink, StringComparison.Ordinal)
                          || !string.Equals(wasTimeZone, existing.TimeZone, StringComparison.Ordinal);

        if (rescheduled)
        {
            await _emailSender.SendMeetingInviteAsync(existing, IcsMethods.Request, IcsSequence.For(existing));
        }

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

        // Cancelled BEFORE the invite goes out, while the attendee list is still readable. A
        // CANCEL with the meeting's UID is what clears it from a calendar; the row staying
        // Cancelled alone leaves it sitting in everyone else's.
        meeting.Status = MeetingStatus.Cancelled;
        meeting.UpdatedAt = DateTime.UtcNow;
        await _meetings.UpdateAsync(meeting);

        await _webhooks.DispatchAsync(tenantId, WebhookEventTypes.MeetingCancelled, meeting);
        await _emailSender.SendMeetingInviteAsync(meeting, IcsMethods.Cancel, IcsSequence.For(meeting) + 1);

        return NoContent();
    }
}
