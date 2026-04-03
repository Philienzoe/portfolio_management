using Ipms.Api.Contracts;
using Ipms.Api.Models;

namespace Ipms.Api.Helpers;

public static class ApiMappings
{
    public static UserSummaryResponse ToSummary(this User user) =>
        new(user.UserId, user.Email, user.FirstName, user.LastName, user.CreatedAt);

    public static UserDetailResponse ToDetail(this User user) =>
        new(
            user.UserId,
            user.Email,
            user.FirstName,
            user.LastName,
            user.CreatedAt,
            user.Portfolios
                .OrderBy(portfolio => portfolio.PortfolioName)
                .Select(portfolio => portfolio.ToSummary())
                .ToList());

    public static PortfolioSummaryResponse ToSummary(this Portfolio portfolio) =>
        new(
            portfolio.PortfolioId,
            portfolio.UserId,
            portfolio.PortfolioName,
            portfolio.Description,
            portfolio.Currency,
            portfolio.CreatedAt);

    public static StockExchangeResponse ToResponse(this StockExchange exchange) =>
        new(
            exchange.ExchangeId,
            exchange.MicCode,
            exchange.Name,
            exchange.Country,
            exchange.City,
            exchange.Timezone);

    public static InstrumentResponse ToResponse(this FinancialInstrument instrument) =>
        new(
            instrument.InstrumentId,
            instrument.TickerSymbol,
            instrument.InstrumentType,
            instrument.Name,
            instrument.CurrentPrice,
            instrument.LastUpdated,
            instrument.Stock?.Exchange?.Name ?? instrument.Etf?.Exchange?.Name,
            instrument.Stock?.Sector,
            instrument.Stock?.Industry,
            instrument.Stock?.MarketCap,
            instrument.Stock?.PeRatio,
            instrument.Stock?.DividendYield,
            instrument.Etf?.AssetClass,
            instrument.Etf?.ExpenseRatio,
            instrument.Etf?.Issuer,
            instrument.Etf?.TrackingIndex,
            instrument.Cryptocurrency?.Blockchain,
            instrument.Cryptocurrency?.HashingAlgorithm,
            instrument.Cryptocurrency?.MaxSupply,
            instrument.Cryptocurrency?.CirculatingSupply);

    public static TransactionResponse ToResponse(this TransactionRecord transaction) =>
        new(
            transaction.TransactionId,
            transaction.PortfolioId,
            transaction.InstrumentId,
            transaction.Instrument.TickerSymbol,
            transaction.Instrument.InstrumentType,
            transaction.Instrument.Name,
            transaction.TransactionType,
            transaction.Quantity,
            transaction.PricePerUnit,
            transaction.TotalAmount == 0 ? transaction.Quantity * transaction.PricePerUnit : transaction.TotalAmount,
            transaction.TransactionDate,
            transaction.Fees,
            transaction.Notes);
}
