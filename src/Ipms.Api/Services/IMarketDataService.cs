namespace Ipms.Api.Services;

public sealed record MarketDataImportCommand(
    string TickerSymbol,
    string Range,
    string Interval,
    bool CreateIfMissing);

public sealed record MarketDataBulkRefreshCommand(
    string Range,
    string Interval,
    string Trigger);

public sealed record MarketDataImportResult(
    bool Success,
    int? InstrumentId,
    string TickerSymbol,
    string? InstrumentType,
    string? Name,
    decimal? CurrentPrice,
    int InsertedPricePoints,
    int UpdatedPricePoints,
    int ProcessedPricePoints,
    int InsertedIntradayPoints,
    int UpdatedIntradayPoints,
    int ProcessedIntradayPoints,
    int InsertedRealtimeSnapshots,
    int UpdatedRealtimeSnapshots,
    int ProcessedRealtimeSnapshots,
    bool CreatedInstrument,
    DateTime ImportedAtUtc,
    string? ErrorMessage);

public sealed record MarketDataBulkRefreshResult(
    string Trigger,
    string Status,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    int InstrumentsAttempted,
    int InstrumentsSucceeded,
    int InstrumentsFailed,
    IReadOnlyList<string> Errors);

public interface IMarketDataService
{
    Task<MarketDataImportResult> ImportByTickerAsync(
        MarketDataImportCommand command,
        CancellationToken cancellationToken = default);

    Task<MarketDataImportResult> RefreshInstrumentAsync(
        int instrumentId,
        string range,
        string interval,
        CancellationToken cancellationToken = default);

    Task<MarketDataBulkRefreshResult> RefreshAllTrackedInstrumentsAsync(
        MarketDataBulkRefreshCommand command,
        CancellationToken cancellationToken = default);
}
