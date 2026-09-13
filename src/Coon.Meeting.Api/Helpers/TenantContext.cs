using System.Security.Claims;

namespace Coon.Meeting.Api.Helpers;

public static class TenantContext
{
    /// <summary>
    /// The authenticated tenant id, from either the ApiKey or ParticipantToken scheme - both
    /// carry a "tenantId" claim. Throws if called on an unauthenticated request; every
    /// controller action that uses this is behind [Authorize].
    /// </summary>
    public static string TenantId(ClaimsPrincipal user) =>
        user.FindFirstValue("tenantId")
        ?? throw new InvalidOperationException("Request has no tenantId claim.");
}
