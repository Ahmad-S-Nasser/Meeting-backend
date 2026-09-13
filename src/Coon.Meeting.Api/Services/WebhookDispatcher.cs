using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coon.Meeting.Api.Models;
using Coon.Meeting.Api.Models.Dtos;
using Coon.Meeting.Api.Repositories;

namespace Coon.Meeting.Api.Services;

public class WebhookDispatcher : IWebhookDispatcher
{
    /// <summary>Seconds to wait before each retry, indexed by (AttemptCount - 1). Six attempts total, then Abandoned.</summary>
    private static readonly int[] BackoffSeconds = { 10, 60, 300, 1800, 7200, 21600 };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ITenantRepository _tenants;
    private readonly IWebhookDeliveryRepository _deliveries;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookDispatcher> _logger;

    public WebhookDispatcher(
        ITenantRepository tenants,
        IWebhookDeliveryRepository deliveries,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookDispatcher> logger)
    {
        _tenants = tenants;
        _deliveries = deliveries;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task DispatchAsync(string tenantId, string eventType, object data)
    {
        try
        {
            var tenant = await _tenants.GetByIdAsync(tenantId);
            if (tenant == null || string.IsNullOrWhiteSpace(tenant.WebhookUrl) || string.IsNullOrWhiteSpace(tenant.WebhookSecret))
                return; // Most tenants may never configure webhooks - not having one is not an error.

            var delivery = new WebhookDelivery { TenantId = tenantId, EventType = eventType };

            var envelope = new WebhookEnvelope
            {
                Id = delivery.Id,
                Type = eventType,
                CreatedAt = delivery.CreatedAt,
                TenantId = tenantId,
                Data = data,
            };
            delivery.PayloadJson = JsonSerializer.Serialize(envelope, JsonOptions);

            await _deliveries.CreateAsync(delivery);
            await AttemptAsync(tenant.WebhookUrl, tenant.WebhookSecret, delivery);
        }
        catch (Exception ex)
        {
            // Never let webhook delivery take down the write that already succeeded.
            _logger.LogWarning(ex, "Webhook dispatch failed for tenant {TenantId} event {EventType}.", tenantId, eventType);
        }
    }

    public Task RetryAsync(WebhookDelivery delivery, Tenant tenant) =>
        AttemptAsync(tenant.WebhookUrl!, tenant.WebhookSecret!, delivery);

    private async Task AttemptAsync(string url, string secret, WebhookDelivery delivery)
    {
        delivery.AttemptCount++;
        delivery.LastAttemptAt = DateTime.UtcNow;

        try
        {
            var client = _httpClientFactory.CreateClient("webhooks");
            var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signature = WebhookSigner.BuildHeader(unixTimestamp, delivery.PayloadJson, secret);

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-CoonMeeting-Event", delivery.EventType);
            request.Headers.Add("X-CoonMeeting-Delivery", delivery.Id);
            request.Headers.Add("X-CoonMeeting-Signature", signature);

            var response = await client.SendAsync(request);
            delivery.LastResponseStatus = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                delivery.Status = WebhookDeliveryStatus.Delivered;
                delivery.DeliveredAt = DateTime.UtcNow;
                delivery.NextAttemptAt = null;
            }
            else
            {
                ScheduleRetryOrAbandon(delivery, $"HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            ScheduleRetryOrAbandon(delivery, ex.Message);
        }

        await _deliveries.UpdateAsync(delivery);
    }

    private static void ScheduleRetryOrAbandon(WebhookDelivery delivery, string error)
    {
        delivery.LastError = error;

        if (delivery.AttemptCount >= BackoffSeconds.Length)
        {
            delivery.Status = WebhookDeliveryStatus.Abandoned;
            delivery.NextAttemptAt = null;
        }
        else
        {
            delivery.NextAttemptAt = DateTime.UtcNow.AddSeconds(BackoffSeconds[delivery.AttemptCount - 1]);
        }
    }
}
