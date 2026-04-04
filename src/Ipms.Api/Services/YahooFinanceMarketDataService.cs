using System.Net;
using System.Net.Http.Json;
using Ipms.Api.Data;
using Ipms.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ipms.Api.Services;

public sealed class YahooFinanceMarketDataService(
    HttpClient httpClient,
    IpmsDbContext dbContext,
    IOptions<MarketDataOptions> options,
    ILogger<YahooFinanceMarketDataService> logger) : IMarketDataService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly IpmsDbContext _dbContext = dbContext;
    private readonly MarketDataOptions _options = options.Value;
    private readonly ILogger<YahooFinanceMarketDataService> _logger = logger;

    public async Task<MarketDataImportResult> ImportByTickerAsync(
        MarketDataImportCommand command,
        CancellationToken cancellationToken = default)
    {
        var ticker = NormalizeTicker(command.TickerSymbol);
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return FailedImport(ticker, "Ticker symbol is required.");
        }

        var instrument = await _dbContext.FinancialInstruments
            .Include(item => item.Stock)
            .Include(item => item.Etf)
            .Include(item => item.Cryptocurrency)
            .SingleOrDefaultAsync(item => item.TickerSymbol == ticker, cancellationToken);

        if (instrument is null && !command.CreateIfMissing)
        {
            return FailedImport(ticker, $"Instrument '{ticker}' was not found.");
        }

        return await RefreshInternalAsync(instrument, ticker, command.Range, command.Interval, command.CreateIfMissing, cancellationToken);
    }

    public async Task<MarketDataImportResult> RefreshInstrumentAsync(
        int instrumentId,
        string range,
        string interval,
        CancellationToken cancellationToken = default)
    {
        var instrument = await _dbContext.FinancialInstruments
            .Include(item => item.Stock)
            .Include(item => item.Etf)
            .Include(item => item.Cryptocurrency)
            .SingleOrDefaultAsync(item => item.InstrumentId == instrumentId, cancellationToken);

        if (instrument is null)
        {
            return FailedImport(null, $"Instrument {instrumentId} was not found.");
        }

        return await RefreshInternalAsync(instrument, instrument.TickerSymbol, range, interval, false, cancellationToken);
    }

    public async Task<MarketDataBulkRefreshResult> RefreshAllTrackedInstrumentsAsync(
        MarketDataBulkRefreshCommand command,
        CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTime.UtcNow;
        var tickers = await _dbContext.FinancialInstruments
            .AsNoTracking()
            .OrderBy(item => item.InstrumentId)
            .Select(item => item.TickerSymbol)
            .ToListAsync(cancellationToken);

        var errors = new List<string>();
        var successCount = 0;

        foreach (var ticker in tickers)
        {
            var result = await ImportByTickerAsync(
                new MarketDataImportCommand(ticker, command.Range, command.Interval, false),
                cancellationToken);

            if (result.Success)
            {
                successCount++;
                continue;
            }

            errors.Add($"{ticker}: {result.ErrorMessage}");
        }

        var failedCount = tickers.Count - successCount;
        var status = failedCount == 0 ? "SUCCESS" : successCount == 0 ? "FAILED" : "PARTIAL";

        return new MarketDataBulkRefreshResult(
            command.Trigger,
            status,
            startedAtUtc,
            DateTime.UtcNow,
            tickers.Count,
            successCount,
            failedCount,
            errors);
    }

    private async Task<MarketDataImportResult> RefreshInternalAsync(
        FinancialInstrument? instrument,
        string ticker,
        string range,
        string interval,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        var chartResult = await FetchChartAsync(ticker, range, interval, cancellationToken);
        if (chartResult.ErrorMessage is not null)
        {
            return FailedImport(ticker, chartResult.ErrorMessage);
        }

        var meta = chartResult.Result!.Meta!;
        var quote = chartResult.Result.Indicators?.Quote?.FirstOrDefault();
        var adjClose = chartResult.Result.Indicators?.Adjclose?.FirstOrDefault();
        var timestamps = chartResult.Result.Timestamp ?? [];
        if (quote is null || timestamps.Length == 0)
        {
            return FailedImport(ticker, "Yahoo Finance returned no chart data.");
        }

        var createdInstrument = false;
        if (instrument is null)
        {
            if (!createIfMissing)
            {
                return FailedImport(ticker, $"Instrument '{ticker}' was not found.");
            }

            instrument = await CreateInstrumentFromYahooAsync(ticker, meta, cancellationToken);
            createdInstrument = true;
        }

        var existingPrices = await _dbContext.HistoricalPrices
            .Where(item => item.InstrumentId == instrument.InstrumentId)
            .ToDictionaryAsync(item => item.PriceDate, cancellationToken);

        var inserted = 0;
        var updated = 0;
        var processed = 0;

        for (var index = 0; index < timestamps.Length; index++)
        {
            var point = TryBuildPricePoint(instrument.InstrumentId, timestamps[index], quote, adjClose, index);
            if (point is null)
            {
                continue;
            }

            processed++;
            if (existingPrices.TryGetValue(point.PriceDate, out var existing))
            {
                existing.OpenPrice = point.OpenPrice;
                existing.HighPrice = point.HighPrice;
                existing.LowPrice = point.LowPrice;
                existing.ClosePrice = point.ClosePrice;
                existing.AdjustedClose = point.AdjustedClose;
                existing.Volume = point.Volume;
                updated++;
            }
            else
            {
                _dbContext.HistoricalPrices.Add(point);
                existingPrices[point.PriceDate] = point;
                inserted++;
            }
        }

        instrument.Name = FirstNonEmpty(meta.LongName, meta.ShortName, instrument.Name, ticker)!;
        if (instrument.Cryptocurrency is not null)
        {
            var cryptoParts = ParseCryptoPair(ticker);
            instrument.Name = NormalizeCryptoName(instrument.Name, cryptoParts.BaseAssetSymbol, cryptoParts.QuoteCurrency);
            instrument.Cryptocurrency.BaseAssetSymbol = FirstNonEmpty(instrument.Cryptocurrency.BaseAssetSymbol, cryptoParts.BaseAssetSymbol);
            instrument.Cryptocurrency.QuoteCurrency = FirstNonEmpty(instrument.Cryptocurrency.QuoteCurrency, cryptoParts.QuoteCurrency);
        }

        instrument.CurrentPrice = meta.RegularMarketPrice ?? quote.Close?.LastOrDefault(value => value is not null);
        instrument.LastUpdated = DateTime.UtcNow;
        ApplyBestEffortMetadata(instrument, meta);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new MarketDataImportResult(
            true,
            instrument.InstrumentId,
            instrument.TickerSymbol,
            instrument.InstrumentType,
            instrument.Name,
            instrument.CurrentPrice,
            inserted,
            updated,
            processed,
            createdInstrument,
            DateTime.UtcNow,
            null);
    }

    private async Task<(YahooFinanceChartResult? Result, string? ErrorMessage)> FetchChartAsync(
        string ticker,
        string range,
        string interval,
        CancellationToken cancellationToken)
    {
        var resolvedRange = string.IsNullOrWhiteSpace(range) ? _options.DefaultRange : range.Trim();
        var resolvedInterval = string.IsNullOrWhiteSpace(interval) ? _options.DefaultInterval : interval.Trim();
        var url = $"v8/finance/chart/{Uri.EscapeDataString(ticker)}?range={Uri.EscapeDataString(resolvedRange)}&interval={Uri.EscapeDataString(resolvedInterval)}&includeAdjustedClose=true";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, $"Ticker '{ticker}' was not found on Yahoo Finance.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return (null, $"Yahoo Finance request failed with status {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<YahooFinanceChartResponse>(cancellationToken: cancellationToken);
        var result = payload?.Chart?.Result?.FirstOrDefault();
        if (result?.Meta is null)
        {
            return (null, payload?.Chart?.Error?.Description ?? "Yahoo Finance did not return market data.");
        }

        return (result, null);
    }

    private async Task<FinancialInstrument> CreateInstrumentFromYahooAsync(
        string ticker,
        YahooFinanceMeta meta,
        CancellationToken cancellationToken)
    {
        var instrumentType = MapInstrumentType(meta.InstrumentType, ticker);
        var instrument = new FinancialInstrument
        {
            TickerSymbol = ticker,
            InstrumentType = instrumentType,
            Name = FirstNonEmpty(meta.LongName, meta.ShortName, ticker)!,
            CurrentPrice = meta.RegularMarketPrice,
            LastUpdated = DateTime.UtcNow
        };

        switch (instrumentType)
        {
            case InstrumentTypes.Stock:
                instrument.Stock = new Stock
                {
                    ExchangeId = await ResolveExchangeIdAsync(meta, cancellationToken),
                    QuoteCurrency = NormalizeQuoteCurrency(meta.Currency)
                };
                break;
            case InstrumentTypes.Etf:
                instrument.Etf = new Etf
                {
                    ExchangeId = await ResolveExchangeIdAsync(meta, cancellationToken),
                    QuoteCurrency = NormalizeQuoteCurrency(meta.Currency)
                };
                break;
            default:
                var cryptoParts = ParseCryptoPair(ticker);
                instrument.Cryptocurrency = new Cryptocurrency();
                instrument.Name = NormalizeCryptoName(instrument.Name, cryptoParts.BaseAssetSymbol, cryptoParts.QuoteCurrency);
                instrument.Cryptocurrency.BaseAssetSymbol = cryptoParts.BaseAssetSymbol;
                instrument.Cryptocurrency.QuoteCurrency = cryptoParts.QuoteCurrency;
                break;
        }

        _dbContext.FinancialInstruments.Add(instrument);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return instrument;
    }

    private void ApplyBestEffortMetadata(FinancialInstrument instrument, YahooFinanceMeta meta)
    {
        if (instrument.Stock is not null && string.IsNullOrWhiteSpace(instrument.Stock.Industry))
        {
            instrument.Stock.Industry = meta.FullExchangeName;
        }

        if (instrument.Stock is not null && string.IsNullOrWhiteSpace(instrument.Stock.QuoteCurrency))
        {
            instrument.Stock.QuoteCurrency = NormalizeQuoteCurrency(meta.Currency);
        }

        if (instrument.Etf is not null && string.IsNullOrWhiteSpace(instrument.Etf.Issuer))
        {
            instrument.Etf.Issuer = meta.FullExchangeName;
        }

        if (instrument.Etf is not null && string.IsNullOrWhiteSpace(instrument.Etf.QuoteCurrency))
        {
            instrument.Etf.QuoteCurrency = NormalizeQuoteCurrency(meta.Currency);
        }

        if (instrument.Cryptocurrency is not null && string.IsNullOrWhiteSpace(instrument.Cryptocurrency.Blockchain))
        {
            instrument.Cryptocurrency.Blockchain = meta.ExchangeName;
        }

        if (instrument.Cryptocurrency is not null)
        {
            var cryptoParts = ParseCryptoPair(instrument.TickerSymbol);
            instrument.Cryptocurrency.BaseAssetSymbol = FirstNonEmpty(instrument.Cryptocurrency.BaseAssetSymbol, cryptoParts.BaseAssetSymbol);
            instrument.Cryptocurrency.QuoteCurrency = FirstNonEmpty(instrument.Cryptocurrency.QuoteCurrency, cryptoParts.QuoteCurrency);
            instrument.Name = NormalizeCryptoName(instrument.Name, cryptoParts.BaseAssetSymbol, cryptoParts.QuoteCurrency);
        }
    }

    private async Task<int?> ResolveExchangeIdAsync(YahooFinanceMeta meta, CancellationToken cancellationToken)
    {
        var labels = new[]
        {
            meta.ExchangeName,
            meta.FullExchangeName
        };

        foreach (var label in labels.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var normalized = label!.ToUpperInvariant();
            if (normalized.Contains("NASDAQ", StringComparison.Ordinal))
            {
                return await FindExchangeIdAsync("XNAS", cancellationToken);
            }

            if (normalized.Contains("NEW YORK", StringComparison.Ordinal) || normalized is "NYQ" or "NYSE")
            {
                return await FindExchangeIdAsync("XNYS", cancellationToken);
            }

            if (normalized.Contains("HONG KONG", StringComparison.Ordinal) || normalized is "HKG")
            {
                return await FindExchangeIdAsync("XHKG", cancellationToken);
            }
        }

        return null;
    }

    private Task<int?> FindExchangeIdAsync(string micCode, CancellationToken cancellationToken) =>
        _dbContext.StockExchanges
            .Where(item => item.MicCode == micCode)
            .Select(item => (int?)item.ExchangeId)
            .SingleOrDefaultAsync(cancellationToken);

    private static HistoricalPrice? TryBuildPricePoint(
        int instrumentId,
        long timestamp,
        YahooFinanceQuote quote,
        YahooFinanceAdjustedClose? adjClose,
        int index)
    {
        var open = GetValue(quote.Open, index);
        var high = GetValue(quote.High, index);
        var low = GetValue(quote.Low, index);
        var close = GetValue(quote.Close, index);
        if (open is null || high is null || low is null || close is null)
        {
            return null;
        }

        return new HistoricalPrice
        {
            InstrumentId = instrumentId,
            PriceDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.Date),
            OpenPrice = open.Value,
            HighPrice = high.Value,
            LowPrice = low.Value,
            ClosePrice = close.Value,
            AdjustedClose = GetValue(adjClose?.Adjclose, index) ?? close.Value,
            Volume = GetValue(quote.Volume, index)
        };
    }

    private static decimal? GetValue(decimal?[]? values, int index) =>
        values is null || index >= values.Length ? null : values[index];

    private static long? GetValue(long?[]? values, int index) =>
        values is null || index >= values.Length ? null : values[index];

    private static string MapInstrumentType(string? yahooInstrumentType, string ticker)
    {
        var normalized = yahooInstrumentType?.Trim().ToUpperInvariant();
        if (normalized is "ETF")
        {
            return InstrumentTypes.Etf;
        }

        if ((normalized?.Contains("CRYPTO", StringComparison.Ordinal) ?? false) ||
            ticker.EndsWith("-USD", StringComparison.OrdinalIgnoreCase))
        {
            return InstrumentTypes.Crypto;
        }

        return InstrumentTypes.Stock;
    }

    private static (string BaseAssetSymbol, string? QuoteCurrency) ParseCryptoPair(string ticker)
    {
        var normalized = NormalizeTicker(ticker);
        var separatorIndex = normalized.LastIndexOf("-");
        if (separatorIndex <= 0 || separatorIndex >= normalized.Length - 1)
        {
            return (normalized, null);
        }

        return (
            normalized[..separatorIndex],
            normalized[(separatorIndex + 1)..]);
    }

    private static string NormalizeCryptoName(string? name, string baseAssetSymbol, string? quoteCurrency)
    {
        var candidate = FirstNonEmpty(name, baseAssetSymbol) ?? baseAssetSymbol;
        if (string.IsNullOrWhiteSpace(quoteCurrency))
        {
            return candidate;
        }

        var suffix = $" {quoteCurrency}";
        return candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? candidate[..^suffix.Length].Trim()
            : candidate;
    }

    private static string NormalizeTicker(string? tickerSymbol) =>
        string.IsNullOrWhiteSpace(tickerSymbol) ? string.Empty : tickerSymbol.Trim().ToUpperInvariant();

    private static string? NormalizeQuoteCurrency(string? quoteCurrency) =>
        string.IsNullOrWhiteSpace(quoteCurrency) ? null : quoteCurrency.Trim().ToUpperInvariant();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static MarketDataImportResult FailedImport(string? ticker, string errorMessage) =>
        new(
            false,
            null,
            ticker ?? string.Empty,
            null,
            null,
            null,
            0,
            0,
            0,
            false,
            DateTime.UtcNow,
            errorMessage);
}
