using System.Security.Claims;
using Ipms.Api.Contracts;
using Ipms.Api.Data;
using Ipms.Api.Helpers;
using Ipms.Api.Models;
using Ipms.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ipms.Api.Endpoints;

public static class PortfolioEndpoints
{
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/users/{userId:int}/portfolios", GetUserPortfolios)
            .WithTags("Portfolios")
            .RequireAuthorization();

        app.MapPost("/users/{userId:int}/portfolios", CreatePortfolio)
            .WithTags("Portfolios")
            .RequireAuthorization();

        var portfolios = app.MapGroup("/portfolios").WithTags("Portfolios").RequireAuthorization();

        portfolios.MapGet("/", GetAllPortfolios);
        portfolios.MapGet("/{portfolioId:int}", GetPortfolioById);
        portfolios.MapGet("/{portfolioId:int}/holdings", GetPortfolioHoldings);
        portfolios.MapGet("/{portfolioId:int}/transactions", GetPortfolioTransactions);
        portfolios.MapPost("/{portfolioId:int}/transactions", RecordTransaction);

        return app;
    }

    private static async Task<IResult> GetUserPortfolios(
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

        var portfolios = await db.Portfolios
            .AsNoTracking()
            .Where(portfolio => portfolio.UserId == userId)
            .OrderBy(portfolio => portfolio.PortfolioName)
            .Select(portfolio => new PortfolioSummaryResponse(
                portfolio.PortfolioId,
                portfolio.UserId,
                portfolio.PortfolioName,
                portfolio.Description,
                portfolio.Currency,
                portfolio.CreatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(portfolios);
    }

    private static async Task<IResult> CreatePortfolio(
        int userId,
        ClaimsPrincipal currentUser,
        CreatePortfolioRequest request,
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        var access = EndpointSecurity.EnsureUserAccess(currentUser, userId);
        if (access is not null)
        {
            return access;
        }

        if (string.IsNullOrWhiteSpace(request.PortfolioName))
        {
            return Results.BadRequest(new { message = "Portfolio name is required." });
        }

        var userExists = await db.Users.AnyAsync(user => user.UserId == userId, cancellationToken);
        if (!userExists)
        {
            return Results.NotFound(new { message = $"User {userId} was not found." });
        }

        var normalizedName = request.PortfolioName.Trim();
        var normalizedCurrency = string.IsNullOrWhiteSpace(request.Currency)
            ? "USD"
            : request.Currency.Trim().ToUpperInvariant();

        var duplicateName = await db.Portfolios.AnyAsync(
            portfolio => portfolio.UserId == userId && portfolio.PortfolioName == normalizedName,
            cancellationToken);

        if (duplicateName)
        {
            return Results.Conflict(new { message = "A portfolio with that name already exists for this user." });
        }

        var portfolio = new Portfolio
        {
            UserId = userId,
            PortfolioName = normalizedName,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Currency = normalizedCurrency,
            CreatedAt = DateTime.UtcNow
        };

        db.Portfolios.Add(portfolio);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/portfolios/{portfolio.PortfolioId}", portfolio.ToSummary());
    }

    private static async Task<IResult> GetAllPortfolios(
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        var portfolios = await db.Portfolios
            .AsNoTracking()
            .OrderBy(portfolio => portfolio.PortfolioId)
            .Select(portfolio => new PortfolioSummaryResponse(
                portfolio.PortfolioId,
                portfolio.UserId,
                portfolio.PortfolioName,
                portfolio.Description,
                portfolio.Currency,
                portfolio.CreatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(portfolios);
    }

    private static async Task<IResult> GetPortfolioById(
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

        var portfolio = await db.Portfolios
            .AsNoTracking()
            .SingleAsync(item => item.PortfolioId == portfolioId, cancellationToken);

        var holdings = await LoadHoldingsAsync(db, portfolioId, cancellationToken);
        var totalMarketValue = holdings.Sum(item => item.MarketValue);

        var response = new PortfolioDetailResponse(
            portfolio.PortfolioId,
            portfolio.UserId,
            portfolio.PortfolioName,
            portfolio.Description,
            portfolio.Currency,
            portfolio.CreatedAt,
            totalMarketValue,
            holdings);

        return Results.Ok(response);
    }

    private static async Task<IResult> GetPortfolioHoldings(
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

        var holdings = await LoadHoldingsAsync(db, portfolioId, cancellationToken);
        return Results.Ok(holdings);
    }

    private static async Task<IResult> GetPortfolioTransactions(
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

        var transactions = await db.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.PortfolioId == portfolioId)
            .Include(transaction => transaction.Instrument)
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenByDescending(transaction => transaction.TransactionId)
            .Select(transaction => new TransactionResponse(
                transaction.TransactionId,
                transaction.PortfolioId,
                transaction.InstrumentId,
                transaction.Instrument.TickerSymbol,
                transaction.Instrument.InstrumentType,
                transaction.Instrument.Name,
                transaction.TransactionType,
                transaction.Quantity,
                transaction.PricePerUnit,
                transaction.Quantity * transaction.PricePerUnit,
                transaction.TransactionDate,
                transaction.Fees,
                transaction.Notes))
            .ToListAsync(cancellationToken);

        return Results.Ok(transactions);
    }

    private static async Task<IResult> RecordTransaction(
        int portfolioId,
        ClaimsPrincipal currentUser,
        CreateTransactionRequest request,
        IpmsDbContext db,
        ITransactionService transactionService,
        CancellationToken cancellationToken)
    {
        var access = await EndpointSecurity.EnsurePortfolioAccessAsync(currentUser, db, portfolioId, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var command = new RecordTransactionCommand(
            portfolioId,
            request.InstrumentId,
            request.TransactionType,
            request.Quantity,
            request.PricePerUnit,
            request.TransactionDate ?? DateTime.UtcNow,
            request.Fees,
            request.Notes);

        var result = await transactionService.RecordTransactionAsync(command, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error switch
            {
                TransactionOperationError.PortfolioNotFound or TransactionOperationError.InstrumentNotFound
                    => Results.NotFound(new { message = result.ErrorMessage }),
                _ => Results.BadRequest(new { message = result.ErrorMessage })
            };
        }

        return Results.Created(
            $"/api/portfolios/{portfolioId}/transactions/{result.Transaction!.TransactionId}",
            result.Transaction.ToResponse());
    }

    private static Task<List<HoldingResponse>> LoadHoldingsAsync(
        IpmsDbContext db,
        int portfolioId,
        CancellationToken cancellationToken) =>
        db.PortfolioHoldings
            .AsNoTracking()
            .Where(holding => holding.PortfolioId == portfolioId)
            .OrderByDescending(holding => holding.Quantity * (holding.Instrument.CurrentPrice ?? 0m))
            .Select(holding => new HoldingResponse(
                holding.PortfolioId,
                holding.InstrumentId,
                holding.Instrument.TickerSymbol,
                holding.Instrument.InstrumentType,
                holding.Instrument.Name,
                holding.Quantity,
                holding.AverageCost,
                holding.Instrument.CurrentPrice,
                holding.Quantity * (holding.Instrument.CurrentPrice ?? 0m),
                holding.LastUpdated))
            .ToListAsync(cancellationToken);
}
