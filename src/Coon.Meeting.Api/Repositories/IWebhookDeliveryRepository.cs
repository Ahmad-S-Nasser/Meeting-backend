using Coon.Meeting.Api.Models;

namespace Coon.Meeting.Api.Repositories;

public interface IWebhookDeliveryRepository
{
    Task CreateAsync(WebhookDelivery delivery);
    Task UpdateAsync(WebhookDelivery delivery);

    /// <summary>Pending deliveries whose NextAttemptAt has arrived, oldest first, capped at <paramref name="limit"/>.</summary>
    Task<List<WebhookDelivery>> GetDueForRetryAsync(DateTime now, int limit);
}
