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

    /// <summary>The meeting a ParticipantToken is scoped to. Only that scheme's tokens carry this claim.</summary>
    public static string MeetingId(ClaimsPrincipal user) =>
        user.FindFirstValue("meetingId")
        ?? throw new InvalidOperationException("Request has no meetingId claim.");

    /// <summary>The integrator's external id for this participant, from the token's "sub" claim.</summary>
    public static string ParticipantId(ClaimsPrincipal user) =>
        user.FindFirstValue("sub")
        ?? throw new InvalidOperationException("Request has no sub claim.");
}
