using Ipms.Api.Data;
using Ipms.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Ipms.Api.Services;

public sealed class TransactionService(IpmsDbContext dbContext) : ITransactionService
{
    private readonly IpmsDbContext _dbContext = dbContext;

    public async Task<TransactionOperationResult> RecordTransactionAsync(
        RecordTransactionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Quantity <= 0)
        {
            return Failure(TransactionOperationError.InvalidQuantity, "Quantity must be greater than zero.");
        }

        if (command.PricePerUnit < 0)
        {
            return Failure(TransactionOperationError.InvalidPrice, "Price per unit cannot be negative.");
        }

        if (command.Fees < 0)
        {
            return Failure(TransactionOperationError.InvalidFees, "Fees cannot be negative.");
        }

        var portfolioExists = await _dbContext.Portfolios
            .AnyAsync(portfolio => portfolio.PortfolioId == command.PortfolioId, cancellationToken);

        if (!portfolioExists)
        {
            return Failure(TransactionOperationError.PortfolioNotFound, "Portfolio was not found.");
        }

        var instrument = await _dbContext.FinancialInstruments
            .SingleOrDefaultAsync(
                item => item.InstrumentId == command.InstrumentId,
                cancellationToken);

        if (instrument is null)
        {
            return Failure(TransactionOperationError.InstrumentNotFound, "Instrument was not found.");
        }

        var holding = await _dbContext.PortfolioHoldings
            .SingleOrDefaultAsync(
                item => item.PortfolioId == command.PortfolioId && item.InstrumentId == command.InstrumentId,
                cancellationToken);

        switch (command.TransactionType)
        {
            case TransactionKind.Buy:
                ApplyBuy(command, holding);
                break;
            case TransactionKind.Sell:
                var sellResult = ApplySell(command, holding);
                if (sellResult is not null)
                {
                    return sellResult;
                }

                break;
            case TransactionKind.Dividend:
                break;
        }

        var transaction = new TransactionRecord
        {
            PortfolioId = command.PortfolioId,
            InstrumentId = command.InstrumentId,
            TransactionType = command.TransactionType,
            Quantity = command.Quantity,
            PricePerUnit = command.PricePerUnit,
            TransactionDate = command.TransactionDate,
            Fees = command.Fees,
            Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim(),
            Instrument = instrument
        };

        _dbContext.Transactions.Add(transaction);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new TransactionOperationResult(transaction, TransactionOperationError.None, null);
    }

    private void ApplyBuy(RecordTransactionCommand command, PortfolioHolding? holding)
    {
        var acquisitionCost = (command.Quantity * command.PricePerUnit) + command.Fees;

        if (holding is null)
        {
            _dbContext.PortfolioHoldings.Add(new PortfolioHolding
            {
                PortfolioId = command.PortfolioId,
                InstrumentId = command.InstrumentId,
                Quantity = command.Quantity,
                AverageCost = acquisitionCost / command.Quantity,
                LastUpdated = command.TransactionDate
            });

            return;
        }

        var existingCostBasis = holding.Quantity * holding.AverageCost;
        var newQuantity = holding.Quantity + command.Quantity;

        holding.AverageCost = (existingCostBasis + acquisitionCost) / newQuantity;
        holding.Quantity = newQuantity;
        holding.LastUpdated = command.TransactionDate;
    }

    private TransactionOperationResult? ApplySell(RecordTransactionCommand command, PortfolioHolding? holding)
    {
        if (holding is null || holding.Quantity < command.Quantity)
        {
            return Failure(
                TransactionOperationError.InsufficientHoldings,
                "Sell transaction exceeds the quantity currently held.");
        }

        var remainingQuantity = holding.Quantity - command.Quantity;
        if (remainingQuantity == 0)
        {
            _dbContext.PortfolioHoldings.Remove(holding);
            return null;
        }

        holding.Quantity = remainingQuantity;
        holding.LastUpdated = command.TransactionDate;
        return null;
    }

    private static TransactionOperationResult Failure(
        TransactionOperationError error,
        string errorMessage) =>
        new(null, error, errorMessage);
}
