using Coon.Meeting.Api.Auth;
using Coon.Meeting.Api.Config;
using Coon.Meeting.Api.Helpers;
using Coon.Meeting.Api.Models;
using Coon.Meeting.Api.Models.Dtos;
using Coon.Meeting.Api.Repositories;
using Coon.Meeting.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coon.Meeting.Api.Controllers;

[ApiController]
[Route("api/v1/tenants")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationSchemeOptions.SchemeName)]
public class TenantsController : ControllerBase
{
    private readonly ITenantRepository _tenants;
    private readonly IApiKeyService _apiKeys;
    private readonly AdminSettings _admin;
    private readonly ILogger<TenantsController> _logger;

    public TenantsController(ITenantRepository tenants, IApiKeyService apiKeys, AdminSettings admin, ILogger<TenantsController> logger)
    {
        _tenants = tenants;
        _apiKeys = apiKeys;
        _admin = admin;
        _logger = logger;
    }

    // GET /api/v1/tenants/me
    [HttpGet("me")]
    public async Task<ActionResult<TenantSelfDto>> Me()
    {
        var tenant = await _tenants.GetByIdAsync(TenantContext.TenantId(User));
        if (tenant == null) return NotFound();

        return Ok(new TenantSelfDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            ApiKeyPrefix = tenant.ApiKeyPrefix,
            WebhookUrl = tenant.WebhookUrl,
            AllowedOrigins = tenant.AllowedOrigins,
            Status = tenant.Status,
            CreatedAt = tenant.CreatedAt,
        });
    }

    // POST /api/v1/tenants - ops-only provisioning, not part of the tenant-facing API surface.
    // No tenant exists yet to hold an ApiKey against, so this can't use the ApiKey scheme the
    // rest of this controller requires - it's gated on a single shared admin key instead.
    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<CreateTenantResponseDto>> Create(
        [FromBody] CreateTenantDto dto,
        [FromHeader(Name = "X-Admin-Provisioning-Key")] string? provisioningKey)
    {
        if (string.IsNullOrEmpty(_admin.ProvisioningKey) || !SecretComparer.Equals(_admin.ProvisioningKey, provisioningKey))
            return Unauthorized();

        var rawKey = _apiKeys.GenerateKey(dto.Live);
        var tenant = new Tenant
        {
            Name = dto.Name,
            ApiKeyHash = _apiKeys.Hash(rawKey),
            ApiKeyPrefix = _apiKeys.Prefix(rawKey),
            WebhookUrl = dto.WebhookUrl,
            WebhookSecret = dto.WebhookSecret,
            AllowedOrigins = dto.AllowedOrigins,
        };

        await _tenants.CreateAsync(tenant);
        _logger.LogInformation("Provisioned tenant {TenantId} ({TenantName}).", tenant.Id, tenant.Name);

        // The only time the raw key is ever available - only its hash is stored, so losing this
        // response means generating a new key, not recovering this one.
        return Ok(new CreateTenantResponseDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            ApiKey = rawKey,
        });
    }
}
