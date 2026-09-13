namespace Coon.Meeting.Api.Models;

public enum WebhookDeliveryStatus
{
    Pending,
    Delivered,
    Abandoned,
}

public class WebhookDelivery
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;

    /// <summary>The exact envelope bytes sent on attempt #1 - never re-serialized on retry, so a retry resends the identical body the signature was computed over.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public int? LastResponseStatus { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }
}
