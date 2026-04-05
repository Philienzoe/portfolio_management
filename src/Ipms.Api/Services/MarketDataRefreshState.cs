namespace Ipms.Api.Services;

public sealed record MarketDataRefreshSnapshot(
    bool Enabled,
    bool IsRunning,
    int RefreshIntervalSeconds,
    string Range,
    string Interval,
    DateTime? LastStartedAtUtc,
    DateTime? LastCompletedAtUtc,
    string? LastTrigger,
    string? LastStatus,
    int InstrumentsAttempted,
    int InstrumentsSucceeded,
    int InstrumentsFailed,
    IReadOnlyList<string> Errors);

public sealed class MarketDataRefreshState
{
    private readonly object _gate = new();
    private bool _enabled;
    private bool _isRunning;
    private int _refreshIntervalSeconds;
    private string _range = "1d";
    private string _interval = "1m";
    private DateTime? _lastStartedAtUtc;
    private DateTime? _lastCompletedAtUtc;
    private string? _lastTrigger;
    private string? _lastStatus;
    private int _instrumentsAttempted;
    private int _instrumentsSucceeded;
    private int _instrumentsFailed;
    private List<string> _errors = [];

    public void ApplyConfiguration(MarketDataOptions options)
    {
        lock (_gate)
        {
            _enabled = options.Scheduler.Enabled;
            _refreshIntervalSeconds = options.Scheduler.RefreshIntervalSeconds;
            _range = options.DefaultRange;
            _interval = options.DefaultInterval;
        }
    }

    public void MarkStarted(string trigger)
    {
        lock (_gate)
        {
            _isRunning = true;
            _lastTrigger = trigger;
            _lastStatus = "RUNNING";
            _lastStartedAtUtc = DateTime.UtcNow;
            _errors = [];
            _instrumentsAttempted = 0;
            _instrumentsSucceeded = 0;
            _instrumentsFailed = 0;
        }
    }

    public void MarkCompleted(MarketDataBulkRefreshResult result)
    {
        lock (_gate)
        {
            _isRunning = false;
            _lastCompletedAtUtc = result.CompletedAtUtc;
            _lastTrigger = result.Trigger;
            _lastStatus = result.Status;
            _instrumentsAttempted = result.InstrumentsAttempted;
            _instrumentsSucceeded = result.InstrumentsSucceeded;
            _instrumentsFailed = result.InstrumentsFailed;
            _errors = result.Errors.ToList();
        }
    }

    public void MarkFailed(string trigger, Exception exception)
    {
        lock (_gate)
        {
            _isRunning = false;
            _lastCompletedAtUtc = DateTime.UtcNow;
            _lastTrigger = trigger;
            _lastStatus = "FAILED";
            _errors = [exception.Message];
        }
    }

    public MarketDataRefreshSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new MarketDataRefreshSnapshot(
                _enabled,
                _isRunning,
                _refreshIntervalSeconds,
                _range,
                _interval,
                _lastStartedAtUtc,
                _lastCompletedAtUtc,
                _lastTrigger,
                _lastStatus,
                _instrumentsAttempted,
                _instrumentsSucceeded,
                _instrumentsFailed,
                _errors.ToList());
        }
    }
}
