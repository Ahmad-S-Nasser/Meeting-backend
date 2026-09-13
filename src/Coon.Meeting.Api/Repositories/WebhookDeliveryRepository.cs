using Coon.Meeting.Api.Models;

namespace Coon.Meeting.Api.Repositories;

public class WebhookDeliveryRepository : IWebhookDeliveryRepository
{
    private readonly LiteDbContext _db;

    public WebhookDeliveryRepository(LiteDbContext db)
    {
        _db = db;
    }

    public Task CreateAsync(WebhookDelivery delivery)
    {
        _db.WebhookDeliveries.Insert(delivery);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(WebhookDelivery delivery)
    {
        _db.WebhookDeliveries.Update(delivery);
        return Task.CompletedTask;
    }

    public Task<List<WebhookDelivery>> GetDueForRetryAsync(DateTime now, int limit)
    {
        var due = _db.WebhookDeliveries
            .Find(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAt != null && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .Take(limit)
            .ToList();

        return Task.FromResult(due);
    }
}
