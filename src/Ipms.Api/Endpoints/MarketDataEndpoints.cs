using Ipms.Api.Contracts;
using Ipms.Api.Services;

namespace Ipms.Api.Endpoints;

public static class MarketDataEndpoints
{
    public static IEndpointRouteBuilder MapMarketDataEndpoints(this IEndpointRouteBuilder app)
    {
        var marketData = app.MapGroup("/market-data").WithTags("Market Data").RequireAuthorization();

        marketData.MapGet("/scheduler-status", GetSchedulerStatus);
        marketData.MapPost("/import/by-ticker", ImportByTicker);
        marketData.MapPost("/import/instruments/{instrumentId:int}", RefreshInstrument);
        marketData.MapPost("/import/all", RefreshAll);

        return app;
    }

    private static IResult GetSchedulerStatus(MarketDataRefreshState state)
    {
        var snapshot = state.GetSnapshot();
        return Results.Ok(new MarketDataSchedulerStatusResponse(
            snapshot.Enabled,
            snapshot.IsRunning,
            snapshot.RefreshIntervalSeconds,
            snapshot.Range,
            snapshot.Interval,
            snapshot.LastStartedAtUtc,
            snapshot.LastCompletedAtUtc,
            snapshot.LastTrigger,
            snapshot.LastStatus,
            snapshot.InstrumentsAttempted,
            snapshot.InstrumentsSucceeded,
            snapshot.InstrumentsFailed,
            snapshot.Errors));
    }

    private static async Task<IResult> ImportByTicker(
        MarketDataImportRequest request,
        IMarketDataService marketDataService,
        CancellationToken cancellationToken)
    {
        var result = await marketDataService.ImportByTickerAsync(
            new MarketDataImportCommand(
                request.TickerSymbol,
                request.Range,
                request.Interval,
                request.CreateIfMissing),
            cancellationToken);

        if (!result.Success)
        {
            return Results.BadRequest(new { message = result.ErrorMessage });
        }

        return Results.Ok(ToResponse(result));
    }

    private static async Task<IResult> RefreshInstrument(
        int instrumentId,
        MarketDataRefreshRequest request,
        IMarketDataService marketDataService,
        CancellationToken cancellationToken)
    {
        var result = await marketDataService.RefreshInstrumentAsync(
            instrumentId,
            request.Range,
            request.Interval,
            cancellationToken);

        if (!result.Success)
        {
            return result.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                ? Results.NotFound(new { message = result.ErrorMessage })
                : Results.BadRequest(new { message = result.ErrorMessage });
        }

        return Results.Ok(ToResponse(result));
    }

    private static async Task<IResult> RefreshAll(
        MarketDataRefreshRequest request,
        IMarketDataService marketDataService,
        MarketDataRefreshState state,
        CancellationToken cancellationToken)
    {
        state.MarkStarted("MANUAL_API");

        var result = await marketDataService.RefreshAllTrackedInstrumentsAsync(
            new MarketDataBulkRefreshCommand(
                request.Range,
                request.Interval,
                "MANUAL_API"),
            cancellationToken);

        state.MarkCompleted(result);

        return Results.Ok(new MarketDataBulkRefreshResponse(
            result.Trigger,
            result.Status,
            result.StartedAtUtc,
            result.CompletedAtUtc,
            result.InstrumentsAttempted,
            result.InstrumentsSucceeded,
            result.InstrumentsFailed,
            result.Errors));
    }

    private static MarketDataImportResponse ToResponse(MarketDataImportResult result) =>
        new(
            result.InstrumentId!.Value,
            result.TickerSymbol,
            result.InstrumentType!,
            result.Name!,
            result.CurrentPrice,
            result.InsertedPricePoints,
            result.UpdatedPricePoints,
            result.ProcessedPricePoints,
            result.CreatedInstrument,
            result.ImportedAtUtc,
            "Yahoo Finance");
}
