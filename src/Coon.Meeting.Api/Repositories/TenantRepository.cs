using Coon.Meeting.Api.Models;

namespace Coon.Meeting.Api.Repositories;

/// <summary>
/// LiteDB has no async driver - it's a single embedded file, so every call here is
/// synchronous I/O wrapped in Task.FromResult to keep the interface awaitable like the rest
/// of the app, not because it actually runs on another thread.
/// </summary>
public class TenantRepository : ITenantRepository
{
    private readonly LiteDbContext _db;

    public TenantRepository(LiteDbContext db)
    {
        _db = db;
    }

    public Task<Tenant?> GetByIdAsync(string id) =>
        Task.FromResult((Tenant?)_db.Tenants.FindById(id));

    public Task<Tenant?> GetByApiKeyHashAsync(string apiKeyHash) =>
        Task.FromResult((Tenant?)_db.Tenants.FindOne(t => t.ApiKeyHash == apiKeyHash));

    public Task CreateAsync(Tenant tenant)
    {
        _db.Tenants.Insert(tenant);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Tenant tenant)
    {
        _db.Tenants.Update(tenant);
        return Task.CompletedTask;
    }
}
