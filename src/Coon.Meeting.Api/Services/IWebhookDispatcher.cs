using Coon.Meeting.Api.Models;

namespace Coon.Meeting.Api.Services;

public interface IWebhookDispatcher
{
    /// <summary>
    /// Fires an event for a tenant, best-effort: never throws, and does nothing at all if the
    /// tenant hasn't configured a webhook. The write that triggered this has already succeeded
    /// and must not be undone by a delivery failure.
    /// </summary>
    Task DispatchAsync(string tenantId, string eventType, object data);

    /// <summary>Re-attempts one already-created delivery, called by WebhookRetryHostedService.</summary>
    Task RetryAsync(WebhookDelivery delivery, Tenant tenant);
}
