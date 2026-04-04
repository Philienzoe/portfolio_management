namespace Ipms.Api.Models;

public enum TransactionKind
{
    Buy,
    Sell,
    Dividend
}

public sealed class TransactionRecord
{
    public int TransactionId { get; set; }
    public int PortfolioId { get; set; }
    public int InstrumentId { get; set; }
    public TransactionKind TransactionType { get; set; }
    public decimal Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
    public DateTime TransactionDate { get; set; }
    public decimal Fees { get; set; }
    public string? Notes { get; set; }

    public Portfolio Portfolio { get; set; } = null!;
    public FinancialInstrument Instrument { get; set; } = null!;
}

public sealed class PortfolioHolding
{
    public int PortfolioId { get; set; }
    public int InstrumentId { get; set; }
    public decimal Quantity { get; set; }
    public decimal AverageCost { get; set; }
    public DateTime LastUpdated { get; set; }

    public Portfolio Portfolio { get; set; } = null!;
    public FinancialInstrument Instrument { get; set; } = null!;
}

public sealed class HistoricalPrice
{
    public int PriceId { get; set; }
    public int InstrumentId { get; set; }
    public DateOnly PriceDate { get; set; }
    public decimal OpenPrice { get; set; }
    public decimal HighPrice { get; set; }
    public decimal LowPrice { get; set; }
    public decimal ClosePrice { get; set; }
    public decimal AdjustedClose { get; set; }
    public long? Volume { get; set; }

    public FinancialInstrument Instrument { get; set; } = null!;
}

public sealed class IntradayPrice
{
    public int IntradayPriceId { get; set; }
    public int InstrumentId { get; set; }
    public DateTime PriceTimeUtc { get; set; }
    public decimal OpenPrice { get; set; }
    public decimal HighPrice { get; set; }
    public decimal LowPrice { get; set; }
    public decimal ClosePrice { get; set; }
    public long? Volume { get; set; }

    public FinancialInstrument Instrument { get; set; } = null!;
}

public sealed class RealtimePriceSnapshot
{
    public int RealtimePriceSnapshotId { get; set; }
    public int InstrumentId { get; set; }
    public DateTime SnapshotTimeUtc { get; set; }
    public DateTime? SourceTimeUtc { get; set; }
    public decimal Price { get; set; }
    public long? Volume { get; set; }

    public FinancialInstrument Instrument { get; set; } = null!;
}
