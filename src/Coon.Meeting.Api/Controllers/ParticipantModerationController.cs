using Coon.Meeting.Api.Auth;
using Coon.Meeting.Api.Helpers;
using Coon.Meeting.Api.Models.Dtos;
using Coon.Meeting.Api.Repositories;
using Coon.Meeting.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coon.Meeting.Api.Controllers;

/// <summary>
/// Organizer-only moderation of a live/future call. Like every other endpoint here, this
/// trusts whatever RequestedByExternalId the API-key-holding caller asserts - Coon.Meeting
/// has no concept of end-user auth of its own, so "is this really the organizer" is only as
/// trustworthy as the integrator's own backend that's making this call.
/// </summary>
[ApiController]
[Route("api/v1/meetings/{meetingId}/participants/{participantExternalId}")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationSchemeOptions.SchemeName)]
public class ParticipantModerationController : ControllerBase
{
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingCallModerationService _moderation;

    public ParticipantModerationController(IMeetingRepository meetings, IMeetingCallModerationService moderation)
    {
        _meetings = meetings;
        _moderation = moderation;
    }

    // POST /api/v1/meetings/{meetingId}/participants/{participantExternalId}/kick - disconnects
    // now; not persisted, so they can rejoin.
    [HttpPost("kick")]
    public async Task<IActionResult> Kick(string meetingId, string participantExternalId, [FromBody] ModerationRequestDto dto)
    {
        var (meeting, error) = await LoadAndAuthorizeAsync(meetingId, dto.RequestedByExternalId);
        if (error != null) return error;

        await _moderation.ForceDisconnectAsync(meetingId, participantExternalId, "Kicked", dto.Reason);
        return NoContent();
    }

    // POST /api/v1/meetings/{meetingId}/participants/{participantExternalId}/block -
    // disconnects now AND persists so they can never rejoin this meeting again.
    [HttpPost("block")]
    public async Task<IActionResult> Block(string meetingId, string participantExternalId, [FromBody] ModerationRequestDto dto)
    {
        var (meeting, error) = await LoadAndAuthorizeAsync(meetingId, dto.RequestedByExternalId);
        if (error != null) return error;

        if (!meeting!.BlockedParticipantIds.Contains(participantExternalId, StringComparer.Ordinal))
        {
            meeting.BlockedParticipantIds.Add(participantExternalId);
            meeting.UpdatedAt = DateTime.UtcNow;
            await _meetings.UpdateAsync(meeting);
        }

        await _moderation.ForceDisconnectAsync(meetingId, participantExternalId, "Blocked", dto.Reason);
        return NoContent();
    }

    // POST /api/v1/meetings/{meetingId}/participants/{participantExternalId}/unblock - lifts a
    // block. Nothing live to disconnect, so no moderation-service call.
    [HttpPost("unblock")]
    public async Task<IActionResult> Unblock(string meetingId, string participantExternalId, [FromBody] ModerationRequestDto dto)
    {
        var (meeting, error) = await LoadAndAuthorizeAsync(meetingId, dto.RequestedByExternalId);
        if (error != null) return error;

        if (meeting!.BlockedParticipantIds.RemoveAll(id => id == participantExternalId) > 0)
        {
            meeting.UpdatedAt = DateTime.UtcNow;
            await _meetings.UpdateAsync(meeting);
        }

        return NoContent();
    }

    private async Task<(Models.Meeting? Meeting, IActionResult? Error)> LoadAndAuthorizeAsync(string meetingId, string requestedByExternalId)
    {
        var tenantId = TenantContext.TenantId(User);
        var meeting = await _meetings.GetByIdAsync(tenantId, meetingId);
        if (meeting == null) return (null, NotFound());

        if (!string.Equals(meeting.CreatedByExternalId, requestedByExternalId, StringComparison.Ordinal))
            return (null, Forbid());

        return (meeting, null);
    }
}
