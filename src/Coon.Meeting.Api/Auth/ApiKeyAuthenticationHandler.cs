using System.Security.Claims;
using System.Text.Encodings.Web;
using Coon.Meeting.Api.Models;
using Coon.Meeting.Api.Repositories;
using Coon.Meeting.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coon.Meeting.Api.Auth;

/// <summary>
/// Server-to-server auth: "Authorization: Bearer sk_live_..." / "sk_test_...". The presented
/// key is SHA-256'd and matched against Tenant.ApiKeyHash - the raw key is never stored, so
/// there is nothing here to compare it against except that hash.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    private readonly ITenantRepository _tenants;
    private readonly IApiKeyService _apiKeys;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITenantRepository tenants,
        IApiKeyService apiKeys)
        : base(options, logger, encoder)
    {
        _tenants = tenants;
        _apiKeys = apiKeys;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValue))
            return AuthenticateResult.NoResult();

        var header = headerValue.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var rawKey = header["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(rawKey) || !rawKey.StartsWith("sk_", StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var hash = _apiKeys.Hash(rawKey);
        var tenant = await _tenants.GetByApiKeyHashAsync(hash);

        if (tenant == null)
            return AuthenticateResult.Fail("Invalid API key.");

        if (tenant.Status != TenantStatus.Active)
            return AuthenticateResult.Fail("This tenant is suspended.");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, tenant.Id),
            new Claim("tenantId", tenant.Id),
            new Claim("tenantName", tenant.Name),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
