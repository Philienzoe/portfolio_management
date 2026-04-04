namespace Ipms.Api.Services;

public sealed class YahooFinanceChartResponse
{
    public YahooFinanceChart? Chart { get; init; }
}

public sealed class YahooFinanceChart
{
    public List<YahooFinanceChartResult>? Result { get; init; }
    public YahooFinanceChartError? Error { get; init; }
}

public sealed class YahooFinanceChartError
{
    public string? Code { get; init; }
    public string? Description { get; init; }
}

public sealed class YahooFinanceChartResult
{
    public YahooFinanceMeta? Meta { get; init; }
    public long[]? Timestamp { get; init; }
    public YahooFinanceIndicators? Indicators { get; init; }
}

public sealed class YahooFinanceMeta
{
    public string? Currency { get; init; }
    public string? Symbol { get; init; }
    public string? ExchangeName { get; init; }
    public string? FullExchangeName { get; init; }
    public string? InstrumentType { get; init; }
    public decimal? RegularMarketPrice { get; init; }
    public long? RegularMarketTime { get; init; }
    public string? LongName { get; init; }
    public string? ShortName { get; init; }
    public string? Timezone { get; init; }
}

public sealed class YahooFinanceIndicators
{
    public List<YahooFinanceQuote>? Quote { get; init; }
    public List<YahooFinanceAdjustedClose>? Adjclose { get; init; }
}

public sealed class YahooFinanceQuote
{
    public decimal?[]? Open { get; init; }
    public decimal?[]? High { get; init; }
    public decimal?[]? Low { get; init; }
    public decimal?[]? Close { get; init; }
    public long?[]? Volume { get; init; }
}

public sealed class YahooFinanceAdjustedClose
{
    public decimal?[]? Adjclose { get; init; }
}
