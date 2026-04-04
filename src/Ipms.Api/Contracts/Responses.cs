using Ipms.Api.Models;

namespace Ipms.Api.Contracts;

public sealed record HealthResponse(
    string Status,
    string Database,
    string DataSource,
    DateTime UtcTime);

public sealed record AuthResponse(
    string TokenType,
    string AccessToken,
    DateTime ExpiresAtUtc,
    UserSummaryResponse User);

public sealed record UserSummaryResponse(
    int UserId,
    string Email,
    string? FirstName,
    string? LastName,
    DateTime CreatedAt);

public sealed record UserDetailResponse(
    int UserId,
    string Email,
    string? FirstName,
    string? LastName,
    DateTime CreatedAt,
    IReadOnlyList<PortfolioSummaryResponse> Portfolios);

public sealed record PortfolioSummaryResponse(
    int PortfolioId,
    int UserId,
    string PortfolioName,
    string? Description,
    string Currency,
    DateTime CreatedAt);

public sealed record PortfolioDetailResponse(
    int PortfolioId,
    int UserId,
    string PortfolioName,
    string? Description,
    string Currency,
    DateTime CreatedAt,
    decimal TotalMarketValue,
    IReadOnlyList<HoldingResponse> Holdings);

public sealed record HoldingResponse(
    int PortfolioId,
    int InstrumentId,
    string TickerSymbol,
    string InstrumentType,
    string Name,
    decimal Quantity,
    decimal AverageCost,
    decimal? CurrentPrice,
    decimal MarketValue,
    DateTime LastUpdated);

public sealed record TransactionResponse(
    int TransactionId,
    int PortfolioId,
    int InstrumentId,
    string TickerSymbol,
    string InstrumentType,
    string Name,
    TransactionKind TransactionType,
    decimal Quantity,
    decimal PricePerUnit,
    decimal TotalAmount,
    DateTime TransactionDate,
    decimal Fees,
    string? Notes);

public sealed record StockExchangeResponse(
    int ExchangeId,
    string MicCode,
    string Name,
    string Country,
    string City,
    string Timezone);

public sealed record InstrumentResponse(
    int InstrumentId,
    string TickerSymbol,
    string InstrumentType,
    string Name,
    decimal? CurrentPrice,
    DateTime LastUpdated,
    string? ExchangeName,
    string? Sector,
    string? Industry,
    decimal? MarketCap,
    decimal? PeRatio,
    decimal? DividendYield,
    string? AssetClass,
    decimal? ExpenseRatio,
    string? Issuer,
    string? TrackingIndex,
    string? QuoteCurrency,
    string? BaseAssetSymbol,
    string? Blockchain,
    string? HashingAlgorithm,
    decimal? MaxSupply,
    decimal? CirculatingSupply);

public sealed record PortfolioValueResponse(
    int PortfolioId,
    string PortfolioName,
    string Currency,
    decimal TotalValue);

public sealed record AllocationResponse(
    string InstrumentType,
    decimal TypeValue,
    decimal AllocationPct);

public sealed record ProfitLossResponse(
    int InstrumentId,
    string TickerSymbol,
    string Name,
    decimal Quantity,
    decimal AverageCost,
    decimal? CurrentPrice,
    decimal UnrealizedPnl,
    decimal? PnlPct);

public sealed record SectorExposureResponse(
    string Sector,
    int NumberOfHoldings,
    decimal SectorValue);

public sealed record HistoricalPerformanceResponse(
    int InstrumentId,
    int Year,
    int Month,
    decimal AverageClose,
    decimal MonthlyHigh,
    decimal MonthlyLow);

public sealed record MarketDataImportResponse(
    int InstrumentId,
    string TickerSymbol,
    string InstrumentType,
    string Name,
    decimal? CurrentPrice,
    int InsertedPricePoints,
    int UpdatedPricePoints,
    int ProcessedPricePoints,
    bool CreatedInstrument,
    DateTime ImportedAtUtc,
    string Source);

public sealed record MarketDataBulkRefreshResponse(
    string Trigger,
    string Status,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    int InstrumentsAttempted,
    int InstrumentsSucceeded,
    int InstrumentsFailed,
    IReadOnlyList<string> Errors);

public sealed record MarketDataSchedulerStatusResponse(
    bool Enabled,
    bool IsRunning,
    int RefreshIntervalSeconds,
    string Range,
    string Interval,
    DateTime? LastStartedAtUtc,
    DateTime? LastCompletedAtUtc,
    string? LastTrigger,
    string? LastStatus,
    int InstrumentsAttempted,
    int InstrumentsSucceeded,
    int InstrumentsFailed,
    IReadOnlyList<string> Errors);
