namespace Ipms.Api.Models;

public static class InstrumentTypes
{
    public const string Stock = "STOCK";
    public const string Etf = "ETF";
    public const string Crypto = "CRYPTO";
}

public sealed class FinancialInstrument
{
    public int InstrumentId { get; set; }
    public string TickerSymbol { get; set; } = string.Empty;
    public string InstrumentType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal? CurrentPrice { get; set; }
    public DateTime LastUpdated { get; set; }

    public Stock? Stock { get; set; }
    public Etf? Etf { get; set; }
    public Cryptocurrency? Cryptocurrency { get; set; }
    public List<TransactionRecord> Transactions { get; set; } = [];
    public List<PortfolioHolding> Holdings { get; set; } = [];
    public List<HistoricalPrice> HistoricalPrices { get; set; } = [];
    public List<IntradayPrice> IntradayPrices { get; set; } = [];
    public List<RealtimePriceSnapshot> RealtimePriceSnapshots { get; set; } = [];
}

public sealed class Stock
{
    public int InstrumentId { get; set; }
    public int? IndustryId { get; set; }
    public string? QuoteCurrency { get; set; }
    public decimal? MarketCap { get; set; }
    public decimal? PeRatio { get; set; }
    public decimal? DividendYield { get; set; }
    public int? ExchangeId { get; set; }

    public FinancialInstrument Instrument { get; set; } = null!;
    public Industry? Industry { get; set; }
    public StockExchange? Exchange { get; set; }
}

public sealed class Etf
{
    public int InstrumentId { get; set; }
    public string? AssetClass { get; set; }
    public decimal? ExpenseRatio { get; set; }
    public string? Issuer { get; set; }
    public string? TrackingIndex { get; set; }
    public string? QuoteCurrency { get; set; }
    public int? ExchangeId { get; set; }

    public FinancialInstrument Instrument { get; set; } = null!;
    public StockExchange? Exchange { get; set; }
}

public sealed class Cryptocurrency
{
    public int InstrumentId { get; set; }
    public string? BaseAssetSymbol { get; set; }
    public string? QuoteCurrency { get; set; }
    public string? Blockchain { get; set; }
    public string? HashingAlgorithm { get; set; }
    public decimal? MaxSupply { get; set; }
    public decimal? CirculatingSupply { get; set; }

    public FinancialInstrument Instrument { get; set; } = null!;
}

public sealed class Sector
{
    public int SectorId { get; set; }
    public string SectorName { get; set; } = string.Empty;

    public List<Industry> Industries { get; set; } = [];
}

public sealed class Industry
{
    public int IndustryId { get; set; }
    public string IndustryName { get; set; } = string.Empty;
    public int SectorId { get; set; }

    public Sector Sector { get; set; } = null!;
    public List<Stock> Stocks { get; set; } = [];
}
