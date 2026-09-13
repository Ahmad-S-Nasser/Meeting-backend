using System.Security.Cryptography;
using System.Text;
using Coon.Meeting.Api.Auth;
using Coon.Meeting.Api.Config;
using Coon.Meeting.Api.Helpers;
using Coon.Meeting.Api.Models.Dtos;
using Coon.Meeting.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coon.Meeting.Api.Controllers;

[ApiController]
[Route("api/v1/meetings/{meetingId}/call-credentials")]
[Authorize(AuthenticationSchemes = ParticipantTokenScheme.SchemeName)]
public class CallCredentialsController : ControllerBase
{
    private readonly TurnSettings _turn;
    private readonly IMeetingRepository _meetings;

    public CallCredentialsController(TurnSettings turn, IMeetingRepository meetings)
    {
        _turn = turn;
        _meetings = meetings;
    }

    [HttpGet]
    public async Task<ActionResult<TurnCredentialsDto>> Get(string meetingId)
    {
        if (TenantContext.MeetingId(User) != meetingId) return Forbid();

        var meeting = await _meetings.GetByIdAsync(TenantContext.TenantId(User), meetingId);
        if (meeting == null) return NotFound();

        var participantId = TenantContext.ParticipantId(User);

        var expiry = DateTimeOffset.UtcNow.AddSeconds(_turn.CredentialTtlSeconds).ToUnixTimeSeconds();
        var username = $"{expiry}:{participantId}";

        using var mac = new HMACSHA1(Encoding.UTF8.GetBytes(_turn.SharedSecret));
        var credential = Convert.ToBase64String(mac.ComputeHash(Encoding.UTF8.GetBytes(username)));

        return Ok(new TurnCredentialsDto
        {
            Username = username,
            Credential = credential,
            Urls = _turn.Urls,
            Ttl = _turn.CredentialTtlSeconds,
        });
    }
}
