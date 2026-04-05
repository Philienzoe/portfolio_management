using Ipms.Api.Models;

namespace Ipms.Api.Contracts;

public sealed record CreateUserRequest(
    string Email,
    string Password,
    string? FirstName,
    string? LastName);

public sealed record RegisterRequest(
    string Email,
    string Password,
    string? FirstName,
    string? LastName);

public sealed record LoginRequest(
    string Email,
    string Password);

public sealed record CreatePortfolioRequest(
    string PortfolioName,
    string? Description,
    string Currency = "USD");

public sealed record CreateTransactionRequest(
    int InstrumentId,
    TransactionKind TransactionType,
    decimal Quantity,
    decimal PricePerUnit,
    DateTime? TransactionDate,
    decimal Fees = 0,
    string? Notes = null);

public sealed record CreateStockRequest(
    string TickerSymbol,
    string Name,
    decimal? CurrentPrice,
    string? Sector,
    string? Industry,
    string? QuoteCurrency,
    decimal? MarketCap,
    decimal? PeRatio,
    decimal? DividendYield,
    int? ExchangeId);

public sealed record CreateEtfRequest(
    string TickerSymbol,
    string Name,
    decimal? CurrentPrice,
    string? AssetClass,
    decimal? ExpenseRatio,
    string? Issuer,
    string? TrackingIndex,
    string? QuoteCurrency,
    int? ExchangeId);

public sealed record CreateCryptocurrencyRequest(
    string TickerSymbol,
    string Name,
    decimal? CurrentPrice,
    string? BaseAssetSymbol,
    string? QuoteCurrency,
    string? Blockchain,
    string? HashingAlgorithm,
    decimal? MaxSupply,
    decimal? CirculatingSupply);

public sealed record AddHistoricalPriceRequest(
    DateOnly PriceDate,
    decimal OpenPrice,
    decimal HighPrice,
    decimal LowPrice,
    decimal ClosePrice,
    decimal AdjustedClose,
    long? Volume);

public sealed record MarketDataImportRequest(
    string TickerSymbol,
    bool CreateIfMissing = true,
    string Range = "1d",
    string Interval = "1m");

public sealed record MarketDataRefreshRequest(
    string Range = "1d",
    string Interval = "1m");
