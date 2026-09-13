namespace Coon.Meeting.Api.Models;

public enum TenantStatus
{
    Active,
    Suspended,
}

public class Tenant
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 hex of the raw API key. The raw key is never stored.</summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    /// <summary>First few characters of the raw key, e.g. "sk_live_ab12" - display only, never enough to authenticate.</summary>
    public string ApiKeyPrefix { get; set; } = string.Empty;

    public string? WebhookUrl { get; set; }

    /// <summary>
    /// Stored plaintext - must be reversible to sign outgoing webhook deliveries, unlike
    /// ApiKeyHash which only ever needs to be compared against.
    /// </summary>
    public string? WebhookSecret { get; set; }

    public List<string> AllowedOrigins { get; set; } = new();
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
