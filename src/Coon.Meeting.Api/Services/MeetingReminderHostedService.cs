using Coon.Meeting.Api.Repositories;

namespace Coon.Meeting.Api.Services;

/// <summary>
/// Fires a meeting.reminder webhook shortly before a meeting starts. Follows the same poll-loop
/// shape as SquadSpace's own MeetingReminderHostedService, dispatching a webhook instead of an
/// in-app notification - there's no shared in-app UI with the integrating product here.
/// </summary>
/// <remarks>
/// SINGLE-INSTANCE ONLY. The atomic claim (LiteMeetingRepository.ClaimMeetingNeedingReminderAsync)
/// only has to be safe within one process on LiteDB - see the single-instance trade-off in the
/// top-level plan - unlike the Mongo-backed original, which was safe across several API instances.
///
/// It never throws. An unhandled exception in a BackgroundService stops the host in .NET 8, so
/// one malformed meeting must not take the whole API down with it.
/// </remarks>
public class MeetingReminderHostedService : BackgroundService
{
    /// <summary>A meeting starting soon is only ever a few minutes away; poll accordingly.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    /// <summary>Matches the VALARM lead time IcsBuilder already uses, so the two stay consistent.</summary>
    private static readonly TimeSpan ReminderWindow = TimeSpan.FromMinutes(15);

    /// <summary>Ceiling per tick, so one busy minute cannot monopolise the loop.</summary>
    private const int MaxPerTick = 25;

    private readonly IServiceProvider _services;
    private readonly ILogger<MeetingReminderHostedService> _logger;

    public MeetingReminderHostedService(IServiceProvider services, ILogger<MeetingReminderHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting before competing with it for the database.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation(
            "Meeting reminder runner started; polling every {Seconds}s for meetings starting within {Minutes}m.",
            PollInterval.TotalSeconds, ReminderWindow.TotalMinutes);

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
                // Never propagate: an unhandled exception here would stop the host.
                _logger.LogError(ex, "Meeting reminder tick failed.");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("Meeting reminder runner stopped.");
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        using var scope = _services.CreateScope();
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingRepository>();
        var webhooks = scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();

        for (var i = 0; i < MaxPerTick && !stoppingToken.IsCancellationRequested; i++)
        {
            var meeting = await meetings.ClaimMeetingNeedingReminderAsync(DateTime.UtcNow, ReminderWindow);
            if (meeting == null) return;

            try
            {
                await webhooks.DispatchAsync(meeting.TenantId, WebhookEventTypes.MeetingReminder, meeting);
            }
            catch (Exception ex)
            {
                // The claim already succeeded (ReminderSentAt is set), so this meeting won't be
                // retried by the next tick even if dispatch fails here - DispatchAsync itself
                // already never throws and schedules its own retry, so reaching this catch would
                // mean something else went wrong entirely.
                _logger.LogWarning(ex, "Failed to dispatch meeting.reminder for meeting {MeetingId}.", meeting.Id);
            }
        }
    }
}
