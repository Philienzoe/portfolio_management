namespace Ipms.Api.Services;

public sealed class MarketDataOptions
{
    public const string SectionName = "MarketData";

    public string BaseUrl { get; init; } = "https://query1.finance.yahoo.com/";
    public string DefaultRange { get; init; } = "1d";
    public string DefaultInterval { get; init; } = "1m";
    public MarketDataSchedulerOptions Scheduler { get; init; } = new();
}

public sealed class MarketDataSchedulerOptions
{
    public bool Enabled { get; init; } = true;
    public bool RunOnStartup { get; init; } = true;
    public int RefreshIntervalSeconds { get; init; } = 60;
}
