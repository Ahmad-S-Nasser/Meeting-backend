namespace Coon.Meeting.Api.Config;

public class TurnSettings
{
    public string SharedSecret { get; set; } = string.Empty;
    public List<string> Urls { get; set; } = new();
    public int CredentialTtlSeconds { get; set; } = 3600;
}
