using Microsoft.AspNetCore.Authentication;

namespace Coon.Meeting.Api.Auth;

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ApiKey";
}

/// <summary>
/// The JWT-bearer scheme name for the short-lived, meeting-scoped participant token minted by
/// POST /meetings/{id}/participant-tokens. It's a standard AddJwtBearer registration (see
/// Program.cs), not a custom handler like ApiKey, so it needs no options class of its own -
/// just this shared name constant.
/// </summary>
public static class ParticipantTokenScheme
{
    public const string SchemeName = "ParticipantToken";
}
