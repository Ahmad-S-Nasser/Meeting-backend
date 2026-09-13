namespace Coon.Meeting.Api.Models.Dtos;

/// <summary>What /tenants/me returns - never ApiKeyHash or WebhookSecret.</summary>
public class TenantSelfDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ApiKeyPrefix { get; set; } = string.Empty;
    public string? WebhookUrl { get; set; }
    public List<string> AllowedOrigins { get; set; } = new();
    public TenantStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
