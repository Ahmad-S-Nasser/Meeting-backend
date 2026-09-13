using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Coon.Meeting.Api.Config;
using Microsoft.IdentityModel.Tokens;

namespace Coon.Meeting.Api.Services;

public class ParticipantTokenService : IParticipantTokenService
{
    private readonly JwtSettings _settings;

    public ParticipantTokenService(JwtSettings settings)
    {
        _settings = settings;
    }

    public ParticipantTokenResult Mint(Models.Meeting meeting, string participantExternalId, string name)
    {
        var now = DateTime.UtcNow;
        var defaultExpiry = now.AddHours(_settings.DefaultTtlHours);

        var expiry = defaultExpiry;
        if (meeting.DurationMinutes is { } minutes)
        {
            var meetingEndCap = meeting.ScheduledAt.AddMinutes(minutes).AddMinutes(30);
            if (meetingEndCap < expiry) expiry = meetingEndCap;
        }

        // A meeting that's already over shouldn't hand back a token with negative TTL.
        if (expiry <= now) expiry = now.AddMinutes(5);

        var claims = new[]
        {
            new Claim("tenantId", meeting.TenantId),
            new Claim("meetingId", meeting.Id),
            new Claim(JwtRegisteredClaimNames.Sub, participantExternalId),
            new Claim("name", name),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.ParticipantKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now,
            expires: expiry,
            signingCredentials: creds);

        return new ParticipantTokenResult
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAt = expiry,
        };
    }
}
