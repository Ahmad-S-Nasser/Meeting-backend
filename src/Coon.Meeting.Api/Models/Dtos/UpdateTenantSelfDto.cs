namespace Coon.Meeting.Api.Models.Dtos;

/// <summary>
/// Partial-update semantics: a null field leaves that field untouched. An empty string
/// clears WebhookUrl/WebhookSecret; an empty list clears AllowedOrigins. Deliberately
/// excludes Status/ApiKeyHash/ApiKeyPrefix - suspension and key rotation stay ops-only.
/// </summary>
public class UpdateTenantSelfDto
{
    public string? Name { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookSecret { get; set; }
    public List<string>? AllowedOrigins { get; set; }
}
