using Coon.Meeting.Api.Repositories;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace Coon.Meeting.Api.Middleware;

/// <summary>
/// Resolves a CORS policy per-request against the union of every tenant's AllowedOrigins,
/// since a single static origin list doesn't work once arbitrary third-party frontends call in
/// - each tenant's integrator runs its own frontend on its own origin, decided at tenant
/// creation time, not at deploy time.
/// </summary>
/// <remarks>
/// No AllowCredentials - this API is never called with cookies (see the frontend SDK's
/// withCredentials: false), so a specific-origin allow list without credentials is enough for
/// both plain fetch calls and the SignalR hub's negotiate request.
/// </remarks>
public class DynamicCorsPolicyProvider : ICorsPolicyProvider
{
    private const string CacheKey = "cors:allowed-origins";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly ITenantRepository _tenants;
    private readonly IMemoryCache _cache;

    public DynamicCorsPolicyProvider(ITenantRepository tenants, IMemoryCache cache)
    {
        _tenants = tenants;
        _cache = cache;
    }

    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) return null;

        var allowedOrigins = await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            var tenants = await _tenants.GetAllAsync();
            return new HashSet<string>(
                tenants.SelectMany(t => t.AllowedOrigins ?? new List<string>()),
                StringComparer.OrdinalIgnoreCase);
        });

        if (allowedOrigins == null || !allowedOrigins.Contains(origin)) return null;

        return new CorsPolicyBuilder(origin)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .Build();
    }
}
