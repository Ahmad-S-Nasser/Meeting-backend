using Coon.Meeting.Api.Auth;
using Coon.Meeting.Api.Helpers;
using Coon.Meeting.Api.Models.Dtos;
using Coon.Meeting.Api.Repositories;
using Coon.Meeting.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coon.Meeting.Api.Controllers;

// Mints tokens for the integrator's own backend to hand to its frontend - only an ApiKey
// holder can call this, never a participant directly, which is why it lives under [Authorize]
// with the ApiKey scheme rather than being open or ParticipantToken-protected itself.
[ApiController]
[Route("api/v1/meetings/{meetingId}/participant-tokens")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationSchemeOptions.SchemeName)]
public class ParticipantTokensController : ControllerBase
{
    private readonly IMeetingRepository _meetings;
    private readonly IParticipantTokenService _tokens;

    public ParticipantTokensController(IMeetingRepository meetings, IParticipantTokenService tokens)
    {
        _meetings = meetings;
        _tokens = tokens;
    }

    [HttpPost]
    public async Task<ActionResult<ParticipantTokenResponseDto>> Mint(string meetingId, [FromBody] MintParticipantTokenDto dto)
    {
        var tenantId = TenantContext.TenantId(User);
        var meeting = await _meetings.GetByIdAsync(tenantId, meetingId);
        if (meeting == null) return NotFound();

        if (meeting.Status == Models.MeetingStatus.Cancelled)
            return BadRequest(new { message = "Cannot mint a call token for a cancelled meeting." });

        var result = _tokens.Mint(meeting, dto.ParticipantExternalId, dto.Name);

        return Ok(new ParticipantTokenResponseDto
        {
            Token = result.Token,
            ExpiresAt = result.ExpiresAt,
            MeetingId = meeting.Id,
        });
    }
}
