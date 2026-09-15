using Coon.Meeting.Api.Models;

namespace Coon.Meeting.Api.Repositories;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(string id);
    Task<Tenant?> GetByApiKeyHashAsync(string apiKeyHash);
    Task CreateAsync(Tenant tenant);
    Task UpdateAsync(Tenant tenant);

    /// <summary>Every tenant - used only to build the CORS allowed-origins set, never exposed on any endpoint.</summary>
    Task<List<Tenant>> GetAllAsync();
}
