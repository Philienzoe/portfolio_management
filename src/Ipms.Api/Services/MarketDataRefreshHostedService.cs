using Microsoft.Extensions.Options;

namespace Ipms.Api.Services;

public sealed class MarketDataRefreshHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<MarketDataOptions> options,
    MarketDataRefreshState state,
    ILogger<MarketDataRefreshHostedService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly MarketDataOptions _options = options.Value;
    private readonly MarketDataRefreshState _state = state;
    private readonly ILogger<MarketDataRefreshHostedService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _state.ApplyConfiguration(_options);

        if (!_options.Scheduler.Enabled)
        {
            _logger.LogInformation("Market data scheduler is disabled.");
            return;
        }

        if (_options.Scheduler.RunOnStartup)
        {
            await RunRefreshAsync("SCHEDULED_STARTUP", stoppingToken);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.Scheduler.RefreshIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunRefreshAsync("SCHEDULED_INTERVAL", stoppingToken);
        }
    }

    private async Task RunRefreshAsync(string trigger, CancellationToken cancellationToken)
    {
        try
        {
            _state.MarkStarted(trigger);

            using var scope = _scopeFactory.CreateScope();
            var marketDataService = scope.ServiceProvider.GetRequiredService<IMarketDataService>();
            var result = await marketDataService.RefreshAllTrackedInstrumentsAsync(
                new MarketDataBulkRefreshCommand(
                    _options.DefaultRange,
                    _options.DefaultInterval,
                    trigger),
                cancellationToken);

            _state.MarkCompleted(result);
            _logger.LogInformation(
                "Market data refresh completed with status {Status}. {Succeeded}/{Attempted} instruments succeeded.",
                result.Status,
                result.InstrumentsSucceeded,
                result.InstrumentsAttempted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.MarkFailed(trigger, exception);
            _logger.LogError(exception, "Scheduled market data refresh failed.");
        }
    }
}
