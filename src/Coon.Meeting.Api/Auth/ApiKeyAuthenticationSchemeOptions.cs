using Microsoft.AspNetCore.Authentication;

namespace Coon.Meeting.Api.Auth;

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ApiKey";
}
