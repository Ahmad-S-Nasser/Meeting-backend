using Coon.Meeting.Api.Models;

namespace Coon.Meeting.Api.Services;

public class ParticipantTokenResult
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public interface IParticipantTokenService
{
    /// <summary>
    /// Mints a JWT scoped to one participant in one meeting. TTL is min(default TTL, meeting
    /// end + 30min) when the meeting's end can be computed, else just the default TTL.
    /// </summary>
    ParticipantTokenResult Mint(Models.Meeting meeting, string participantExternalId, string name);
}
