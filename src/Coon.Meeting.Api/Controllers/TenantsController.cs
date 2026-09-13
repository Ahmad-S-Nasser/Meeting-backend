using Coon.Meeting.Api.Auth;
using Coon.Meeting.Api.Helpers;
using Coon.Meeting.Api.Models.Dtos;
using Coon.Meeting.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coon.Meeting.Api.Controllers;

[ApiController]
[Route("api/v1/tenants")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationSchemeOptions.SchemeName)]
public class TenantsController : ControllerBase
{
    private readonly ITenantRepository _tenants;

    public TenantsController(ITenantRepository tenants)
    {
        _tenants = tenants;
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
}
