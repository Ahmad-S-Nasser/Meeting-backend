namespace Coon.Meeting.Api.Config;

public class JwtSettings
{
    public string Issuer { get; set; } = "coon-meeting";
    public string Audience { get; set; } = "coon-meeting-participants";
    public string ParticipantKey { get; set; } = string.Empty;

    /// <summary>Default participant-token TTL. Capped further at meeting end + 30min when the meeting's duration is known.</summary>
    public int DefaultTtlHours { get; set; } = 4;
}
