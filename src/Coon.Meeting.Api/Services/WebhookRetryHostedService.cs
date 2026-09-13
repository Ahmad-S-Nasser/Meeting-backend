using Coon.Meeting.Api.Models;
using Coon.Meeting.Api.Repositories;

namespace Coon.Meeting.Api.Services;

/// <summary>
/// Retries Pending webhook deliveries whose NextAttemptAt has arrived, per the backoff schedule
/// in WebhookDispatcher. Follows the same poll-loop shape as MeetingReminderHostedService.
/// </summary>
/// <remarks>
/// SINGLE-INSTANCE ONLY, unlike the Mongo-backed reminder poller this pattern is copied from -
/// see the LiteDB single-writer trade-off in the top-level plan. No atomic claim is needed here
/// because only one process ever touches the file.
///
/// It never throws. An unhandled exception in a BackgroundService stops the host in .NET 8, so
/// one bad delivery must not take the whole API down with it.
/// </remarks>
public class WebhookRetryHostedService : BackgroundService
{
    /// <summary>Matches the dispatcher's shortest backoff step (10s), so a fast-failing endpoint's retry isn't delayed by a slow poll.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private const int MaxPerTick = 25;

    private readonly IServiceProvider _services;
    private readonly ILogger<WebhookRetryHostedService> _logger;

    public WebhookRetryHostedService(IServiceProvider services, ILogger<WebhookRetryHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("Webhook retry runner started; polling every {Seconds}s.", PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook retry tick failed.");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("Webhook retry runner stopped.");
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        using var scope = _services.CreateScope();
        var deliveries = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryRepository>();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();

        var due = await deliveries.GetDueForRetryAsync(DateTime.UtcNow, MaxPerTick);

        foreach (var delivery in due)
        {
            if (stoppingToken.IsCancellationRequested) return;

            var tenant = await tenants.GetByIdAsync(delivery.TenantId);
            if (tenant == null || string.IsNullOrWhiteSpace(tenant.WebhookUrl) || string.IsNullOrWhiteSpace(tenant.WebhookSecret))
            {
                delivery.Status = WebhookDeliveryStatus.Abandoned;
                delivery.LastError = "Tenant webhook is no longer configured.";
                delivery.NextAttemptAt = null;
                await deliveries.UpdateAsync(delivery);
                continue;
            }

            try
            {
                await dispatcher.RetryAsync(delivery, tenant);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Webhook retry failed for delivery {DeliveryId}.", delivery.Id);
            }
        }
    }
}
