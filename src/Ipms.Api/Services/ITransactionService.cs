using Ipms.Api.Models;

namespace Ipms.Api.Services;

public sealed record RecordTransactionCommand(
    int PortfolioId,
    int InstrumentId,
    TransactionKind TransactionType,
    decimal Quantity,
    decimal PricePerUnit,
    DateTime TransactionDate,
    decimal Fees,
    string? Notes);

public enum TransactionOperationError
{
    None,
    PortfolioNotFound,
    InstrumentNotFound,
    InvalidQuantity,
    InvalidPrice,
    InvalidFees,
    InsufficientHoldings
}

public sealed record TransactionOperationResult(
    TransactionRecord? Transaction,
    TransactionOperationError Error,
    string? ErrorMessage)
{
    public bool Succeeded => Error == TransactionOperationError.None;
}

public interface ITransactionService
{
    Task<TransactionOperationResult> RecordTransactionAsync(
        RecordTransactionCommand command,
        CancellationToken cancellationToken = default);
}
