namespace Coon.Meeting.Api.Models.Dtos;

public class WebhookEnvelope
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public object? Data { get; set; }
}
