using System.Security.Claims;
using Ipms.Api.Contracts;
using Ipms.Api.Data;
using Ipms.Api.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Ipms.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var analytics = app.MapGroup("/analytics").WithTags("Analytics").RequireAuthorization();

        analytics.MapGet("/users/{userId:int}/portfolio-values", GetPortfolioValues);
        analytics.MapGet("/portfolios/{portfolioId:int}/allocation", GetAssetAllocation);
        analytics.MapGet("/portfolios/{portfolioId:int}/profit-loss", GetProfitLoss);
        analytics.MapGet("/portfolios/{portfolioId:int}/sector-exposure", GetSectorExposure);
        analytics.MapGet("/instruments/{instrumentId:int}/historical-performance", GetHistoricalPerformance);

        return app;
    }

    private static async Task<IResult> GetPortfolioValues(
        int userId,
        ClaimsPrincipal currentUser,
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        var access = EndpointSecurity.EnsureUserAccess(currentUser, userId);
        if (access is not null)
        {
            return access;
        }

        var userExists = await db.Users.AnyAsync(user => user.UserId == userId, cancellationToken);
        if (!userExists)
        {
            return Results.NotFound(new { message = $"User {userId} was not found." });
        }

        var values = await db.Portfolios
            .AsNoTracking()
            .Where(portfolio => portfolio.UserId == userId)
            .Select(portfolio => new
            {
                portfolio.PortfolioId,
                portfolio.PortfolioName,
                portfolio.Currency,
                TotalValue = portfolio.Holdings.Sum(holding => holding.Quantity * (holding.Instrument.CurrentPrice ?? 0m))
            })
            .OrderByDescending(item => item.TotalValue)
            .ToListAsync(cancellationToken);

        return Results.Ok(values.Select(item => new PortfolioValueResponse(
            item.PortfolioId,
            item.PortfolioName,
            item.Currency,
            item.TotalValue)));
    }

    private static async Task<IResult> GetAssetAllocation(
        int portfolioId,
        ClaimsPrincipal currentUser,
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        var access = await EndpointSecurity.EnsurePortfolioAccessAsync(currentUser, db, portfolioId, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var typeValues = await db.PortfolioHoldings
            .AsNoTracking()
            .Where(holding => holding.PortfolioId == portfolioId)
            .GroupBy(holding => holding.Instrument.InstrumentType)
            .Select(group => new
            {
                InstrumentType = group.Key,
                TypeValue = group.Sum(item => item.Quantity * (item.Instrument.CurrentPrice ?? 0m))
            })
            .OrderByDescending(item => item.TypeValue)
            .ToListAsync(cancellationToken);

        var totalValue = typeValues.Sum(item => item.TypeValue);
        var result = typeValues
            .Select(item => new AllocationResponse(
                item.InstrumentType,
                item.TypeValue,
                totalValue == 0 ? 0 : Math.Round(item.TypeValue * 100m / totalValue, 2)))
            .ToList();

        return Results.Ok(result);
    }

    private static async Task<IResult> GetProfitLoss(
        int portfolioId,
        ClaimsPrincipal currentUser,
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        var access = await EndpointSecurity.EnsurePortfolioAccessAsync(currentUser, db, portfolioId, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var result = await db.PortfolioHoldings
            .AsNoTracking()
            .Where(holding => holding.PortfolioId == portfolioId)
            .Select(holding => new
            {
                holding.InstrumentId,
                holding.Instrument.TickerSymbol,
                holding.Instrument.Name,
                holding.Quantity,
                holding.AverageCost,
                holding.Instrument.CurrentPrice,
                UnrealizedPnl = ((holding.Instrument.CurrentPrice ?? 0m) - holding.AverageCost) * holding.Quantity,
                PnlPct = holding.AverageCost == 0
                    ? (decimal?)null
                    : Math.Round((((holding.Instrument.CurrentPrice ?? 0m) - holding.AverageCost) / holding.AverageCost) * 100m, 2)
            })
            .OrderByDescending(item => item.UnrealizedPnl)
            .ToListAsync(cancellationToken);

        return Results.Ok(result.Select(item => new ProfitLossResponse(
            item.InstrumentId,
            item.TickerSymbol,
            item.Name,
            item.Quantity,
            item.AverageCost,
            item.CurrentPrice,
            item.UnrealizedPnl,
            item.PnlPct)));
    }

    private static async Task<IResult> GetSectorExposure(
        int portfolioId,
        ClaimsPrincipal currentUser,
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        var access = await EndpointSecurity.EnsurePortfolioAccessAsync(currentUser, db, portfolioId, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var result = await (
            from holding in db.PortfolioHoldings.AsNoTracking()
            join stock in db.Stocks.AsNoTracking() on holding.InstrumentId equals stock.InstrumentId
            join instrument in db.FinancialInstruments.AsNoTracking() on holding.InstrumentId equals instrument.InstrumentId
            where holding.PortfolioId == portfolioId
            group new { holding, stock, instrument } by stock.Sector ?? "Unclassified" into grouped
            orderby grouped.Sum(item => item.holding.Quantity * (item.instrument.CurrentPrice ?? 0m)) descending
            select new
            {
                Sector = grouped.Key,
                NumberOfHoldings = grouped.Count(),
                SectorValue = grouped.Sum(item => item.holding.Quantity * (item.instrument.CurrentPrice ?? 0m))
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(result.Select(item => new SectorExposureResponse(
            item.Sector,
            item.NumberOfHoldings,
            item.SectorValue)));
    }

    private static async Task<IResult> GetHistoricalPerformance(
        int instrumentId,
        ClaimsPrincipal currentUser,
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        if (currentUser.GetCurrentUserId() is null)
        {
            return Results.Unauthorized();
        }

        var instrumentExists = await db.FinancialInstruments.AnyAsync(
            instrument => instrument.InstrumentId == instrumentId,
            cancellationToken);

        if (!instrumentExists)
        {
            return Results.NotFound(new { message = $"Instrument {instrumentId} was not found." });
        }

        var prices = await db.HistoricalPrices
            .AsNoTracking()
            .Where(price => price.InstrumentId == instrumentId)
            .OrderBy(price => price.PriceDate)
            .ToListAsync(cancellationToken);

        var result = prices
            .GroupBy(price => new { price.PriceDate.Year, price.PriceDate.Month })
            .Select(group => new HistoricalPerformanceResponse(
                instrumentId,
                group.Key.Year,
                group.Key.Month,
                Math.Round(group.Average(item => item.ClosePrice), 4),
                group.Max(item => item.HighPrice),
                group.Min(item => item.LowPrice)))
            .OrderBy(item => item.Year)
            .ThenBy(item => item.Month)
            .ToList();

        return Results.Ok(result);
    }
}
