namespace Coon.Meeting.Api.Models.Dtos;

/// <summary>
/// Short-lived TURN credentials for one meeting call, minted per the standard coturn REST API
/// long-term-credential convention: username = "{expiry}:{participantId}", credential =
/// base64(HMAC-SHA1(sharedSecret, username)). The shared secret itself never leaves the server.
/// </summary>
public class TurnCredentialsDto
{
    public string Username { get; set; } = string.Empty;
    public string Credential { get; set; } = string.Empty;
    public List<string> Urls { get; set; } = new();
    public int Ttl { get; set; }
}
