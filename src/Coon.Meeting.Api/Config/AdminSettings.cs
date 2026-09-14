namespace Coon.Meeting.Api.Config;

public class AdminSettings
{
    /// <summary>Gates POST /tenants - ops-only tenant provisioning, not a per-tenant credential.</summary>
    public string ProvisioningKey { get; set; } = string.Empty;
}
