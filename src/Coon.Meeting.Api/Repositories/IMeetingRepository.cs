using Coon.Meeting.Api.Models;

namespace Coon.Meeting.Api.Repositories;

public interface IMeetingRepository
{
    Task<List<Models.Meeting>> GetByTenantIdAsync(string tenantId);

    /// <summary>Scoped by tenant so one tenant's key can never read another's meeting by id.</summary>
    Task<Models.Meeting?> GetByIdAsync(string tenantId, string id);

    Task CreateAsync(Models.Meeting meeting);
    Task UpdateAsync(Models.Meeting meeting);
}
