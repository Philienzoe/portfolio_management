using Ipms.Api.Contracts;
using Ipms.Api.Data;
using Ipms.Api.Helpers;
using Ipms.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Ipms.Api.Endpoints;

public static class InstrumentEndpoints
{
    public static IEndpointRouteBuilder MapInstrumentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/stock-exchanges", GetStockExchanges)
            .WithTags("Stock Exchanges");

        var instruments = app.MapGroup("/instruments").WithTags("Instruments");

        instruments.MapGet("/", GetInstruments);
        instruments.MapGet("/{instrumentId:int}", GetInstrumentById);
        instruments.MapPost("/stocks", CreateStock).RequireAuthorization();
        instruments.MapPost("/etfs", CreateEtf).RequireAuthorization();
        instruments.MapPost("/cryptocurrencies", CreateCryptocurrency).RequireAuthorization();
        instruments.MapPost("/{instrumentId:int}/prices", AddHistoricalPrice).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetStockExchanges(
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        var exchanges = await db.StockExchanges
            .AsNoTracking()
            .OrderBy(exchange => exchange.Name)
            .Select(exchange => new StockExchangeResponse(
                exchange.ExchangeId,
                exchange.MicCode,
                exchange.Name,
                exchange.Country,
                exchange.City,
                exchange.Timezone))
            .ToListAsync(cancellationToken);

        return Results.Ok(exchanges);
    }

    private static async Task<IResult> GetInstruments(
        string? type,
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        var normalizedType = string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToUpperInvariant();
        if (normalizedType is not null &&
            normalizedType is not InstrumentTypes.Stock &&
            normalizedType is not InstrumentTypes.Etf &&
            normalizedType is not InstrumentTypes.Crypto)
        {
            return Results.BadRequest(new { message = "Instrument type must be STOCK, ETF, or CRYPTO." });
        }

        var query = IncludeInstrumentGraph(db.FinancialInstruments.AsNoTracking());
        if (normalizedType is not null)
        {
            query = query.Where(instrument => instrument.InstrumentType == normalizedType);
        }

        var instruments = await query
            .OrderBy(instrument => instrument.TickerSymbol)
            .ToListAsync(cancellationToken);

        return Results.Ok(instruments.Select(instrument => instrument.ToResponse()));
    }

    private static async Task<IResult> GetInstrumentById(
        int instrumentId,
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        var instrument = await LoadInstrumentAsync(db, instrumentId, cancellationToken);

        return instrument is null
            ? Results.NotFound(new { message = $"Instrument {instrumentId} was not found." })
            : Results.Ok(instrument.ToResponse());
    }

    private static async Task<IResult> CreateStock(
        CreateStockRequest request,
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TickerSymbol) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { message = "Ticker symbol and name are required." });
        }

        var ticker = request.TickerSymbol.Trim().ToUpperInvariant();
        if (await db.FinancialInstruments.AnyAsync(item => item.TickerSymbol == ticker, cancellationToken))
        {
            return Results.Conflict(new { message = $"Instrument '{ticker}' already exists." });
        }

        if (request.ExchangeId is not null &&
            !await db.StockExchanges.AnyAsync(exchange => exchange.ExchangeId == request.ExchangeId, cancellationToken))
        {
            return Results.BadRequest(new { message = $"Exchange {request.ExchangeId} was not found." });
        }

        var stockQuoteCurrency = NormalizeQuoteCurrency(request.QuoteCurrency) ??
            await ResolveQuoteCurrencyFromExchangeAsync(db, request.ExchangeId, cancellationToken);
        int? industryId;
        try
        {
            industryId = await ResolveIndustryIdAsync(db, request.Sector, request.Industry, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }

        var instrument = new FinancialInstrument
        {
            TickerSymbol = ticker,
            InstrumentType = InstrumentTypes.Stock,
            Name = request.Name.Trim(),
            CurrentPrice = request.CurrentPrice,
            LastUpdated = DateTime.UtcNow,
            Stock = new Stock
            {
                IndustryId = industryId,
                QuoteCurrency = stockQuoteCurrency,
                MarketCap = request.MarketCap,
                PeRatio = request.PeRatio,
                DividendYield = request.DividendYield,
                ExchangeId = request.ExchangeId
            }
        };

        db.FinancialInstruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var savedInstrument = await LoadInstrumentAsync(db, instrument.InstrumentId, cancellationToken);
        return Results.Created($"/api/instruments/{instrument.InstrumentId}", savedInstrument!.ToResponse());
    }

    private static async Task<IResult> CreateEtf(
        CreateEtfRequest request,
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TickerSymbol) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { message = "Ticker symbol and name are required." });
        }

        var ticker = request.TickerSymbol.Trim().ToUpperInvariant();
        if (await db.FinancialInstruments.AnyAsync(item => item.TickerSymbol == ticker, cancellationToken))
        {
            return Results.Conflict(new { message = $"Instrument '{ticker}' already exists." });
        }

        if (request.ExchangeId is not null &&
            !await db.StockExchanges.AnyAsync(exchange => exchange.ExchangeId == request.ExchangeId, cancellationToken))
        {
            return Results.BadRequest(new { message = $"Exchange {request.ExchangeId} was not found." });
        }

        var etfQuoteCurrency = NormalizeQuoteCurrency(request.QuoteCurrency) ??
            await ResolveQuoteCurrencyFromExchangeAsync(db, request.ExchangeId, cancellationToken);

        var instrument = new FinancialInstrument
        {
            TickerSymbol = ticker,
            InstrumentType = InstrumentTypes.Etf,
            Name = request.Name.Trim(),
            CurrentPrice = request.CurrentPrice,
            LastUpdated = DateTime.UtcNow,
            Etf = new Etf
            {
                AssetClass = request.AssetClass?.Trim(),
                ExpenseRatio = request.ExpenseRatio,
                Issuer = request.Issuer?.Trim(),
                TrackingIndex = request.TrackingIndex?.Trim(),
                QuoteCurrency = etfQuoteCurrency,
                ExchangeId = request.ExchangeId
            }
        };

        db.FinancialInstruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var savedInstrument = await LoadInstrumentAsync(db, instrument.InstrumentId, cancellationToken);
        return Results.Created($"/api/instruments/{instrument.InstrumentId}", savedInstrument!.ToResponse());
    }

    private static async Task<IResult> CreateCryptocurrency(
        CreateCryptocurrencyRequest request,
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TickerSymbol) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { message = "Ticker symbol and name are required." });
        }

        var ticker = request.TickerSymbol.Trim().ToUpperInvariant();
        if (await db.FinancialInstruments.AnyAsync(item => item.TickerSymbol == ticker, cancellationToken))
        {
            return Results.Conflict(new { message = $"Instrument '{ticker}' already exists." });
        }

        var (baseAssetSymbol, quoteCurrency) = ParseCryptoPair(ticker);
        var normalizedQuoteCurrency = request.QuoteCurrency?.Trim().ToUpperInvariant() ?? quoteCurrency;
        var normalizedName = NormalizeCryptoName(request.Name, normalizedQuoteCurrency);

        var instrument = new FinancialInstrument
        {
            TickerSymbol = ticker,
            InstrumentType = InstrumentTypes.Crypto,
            Name = normalizedName,
            CurrentPrice = request.CurrentPrice,
            LastUpdated = DateTime.UtcNow,
            Cryptocurrency = new Cryptocurrency
            {
                BaseAssetSymbol = request.BaseAssetSymbol?.Trim().ToUpperInvariant() ?? baseAssetSymbol,
                QuoteCurrency = normalizedQuoteCurrency,
                Blockchain = request.Blockchain?.Trim(),
                HashingAlgorithm = request.HashingAlgorithm?.Trim(),
                MaxSupply = request.MaxSupply,
                CirculatingSupply = request.CirculatingSupply
            }
        };

        db.FinancialInstruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var savedInstrument = await LoadInstrumentAsync(db, instrument.InstrumentId, cancellationToken);
        return Results.Created($"/api/instruments/{instrument.InstrumentId}", savedInstrument!.ToResponse());
    }

    private static async Task<IResult> AddHistoricalPrice(
        int instrumentId,
        AddHistoricalPriceRequest request,
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        if (request.HighPrice < request.LowPrice || request.OpenPrice < 0 || request.ClosePrice < 0 || request.AdjustedClose < 0)
        {
            return Results.BadRequest(new { message = "Price values are invalid." });
        }

        var instrument = await db.FinancialInstruments
            .SingleOrDefaultAsync(item => item.InstrumentId == instrumentId, cancellationToken);

        if (instrument is null)
        {
            return Results.NotFound(new { message = $"Instrument {instrumentId} was not found." });
        }

        var existingPrice = await db.HistoricalPrices.AnyAsync(
            item => item.InstrumentId == instrumentId && item.PriceDate == request.PriceDate,
            cancellationToken);

        if (existingPrice)
        {
            return Results.Conflict(new { message = "A price entry already exists for that instrument and date." });
        }

        var price = new HistoricalPrice
        {
            InstrumentId = instrumentId,
            PriceDate = request.PriceDate,
            OpenPrice = request.OpenPrice,
            HighPrice = request.HighPrice,
            LowPrice = request.LowPrice,
            ClosePrice = request.ClosePrice,
            AdjustedClose = request.AdjustedClose,
            Volume = request.Volume
        };

        instrument.CurrentPrice = request.ClosePrice;
        instrument.LastUpdated = DateTime.UtcNow;

        db.HistoricalPrices.Add(price);
        await db.SaveChangesAsync(cancellationToken);

        var savedInstrument = await LoadInstrumentAsync(db, instrumentId, cancellationToken);
        return Results.Created($"/api/instruments/{instrumentId}", savedInstrument!.ToResponse());
    }

    private static IQueryable<FinancialInstrument> IncludeInstrumentGraph(IQueryable<FinancialInstrument> query) =>
        query
            .Include(instrument => instrument.Stock)
                .ThenInclude(stock => stock!.Industry)
                    .ThenInclude(industry => industry!.Sector)
            .Include(instrument => instrument.Stock)
                .ThenInclude(stock => stock!.Exchange)
            .Include(instrument => instrument.Etf)
                .ThenInclude(etf => etf!.Exchange)
            .Include(instrument => instrument.Cryptocurrency);

    private static Task<FinancialInstrument?> LoadInstrumentAsync(
        IpmsDbContext db,
        int instrumentId,
        CancellationToken cancellationToken) =>
        IncludeInstrumentGraph(db.FinancialInstruments.AsNoTracking())
            .SingleOrDefaultAsync(item => item.InstrumentId == instrumentId, cancellationToken);

    private static string? NormalizeQuoteCurrency(string? quoteCurrency) =>
        string.IsNullOrWhiteSpace(quoteCurrency) ? null : quoteCurrency.Trim().ToUpperInvariant();

    private static async Task<string?> ResolveQuoteCurrencyFromExchangeAsync(
        IpmsDbContext db,
        int? exchangeId,
        CancellationToken cancellationToken)
    {
        if (exchangeId is null)
        {
            return null;
        }

        var micCode = await db.StockExchanges
            .Where(exchange => exchange.ExchangeId == exchangeId.Value)
            .Select(exchange => exchange.MicCode)
            .SingleOrDefaultAsync(cancellationToken);

        return MapQuoteCurrencyFromMicCode(micCode);
    }

    private static string? MapQuoteCurrencyFromMicCode(string? micCode) =>
        micCode switch
        {
            "XHKG" => "HKD",
            "XNAS" or "XNYS" => "USD",
            _ => null
        };

    private static async Task<int?> ResolveIndustryIdAsync(
        IpmsDbContext db,
        string? sectorName,
        string? industryName,
        CancellationToken cancellationToken)
    {
        var normalizedIndustry = NormalizeReferenceName(industryName);
        var normalizedSector = NormalizeReferenceName(sectorName);

        if (normalizedIndustry is null && normalizedSector is null)
        {
            return null;
        }

        var resolvedSectorName = normalizedSector ?? "Unclassified";
        var resolvedIndustryName = normalizedIndustry ?? $"{resolvedSectorName} General";

        var existingIndustry = await db.Industries
            .SingleOrDefaultAsync(industry => industry.IndustryName == resolvedIndustryName, cancellationToken);

        if (existingIndustry is not null)
        {
            if (normalizedSector is not null)
            {
                var currentSectorName = await db.Sectors
                    .Where(sector => sector.SectorId == existingIndustry.SectorId)
                    .Select(sector => sector.SectorName)
                    .SingleAsync(cancellationToken);

                if (!string.Equals(currentSectorName, resolvedSectorName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Industry '{resolvedIndustryName}' already exists under sector '{currentSectorName}'.");
                }
            }

            return existingIndustry.IndustryId;
        }

        var sector = await db.Sectors
            .SingleOrDefaultAsync(item => item.SectorName == resolvedSectorName, cancellationToken);

        if (sector is null)
        {
            sector = new Sector
            {
                SectorName = resolvedSectorName
            };
            db.Sectors.Add(sector);
            await db.SaveChangesAsync(cancellationToken);
        }

        var industry = new Industry
        {
            IndustryName = resolvedIndustryName,
            SectorId = sector.SectorId
        };

        db.Industries.Add(industry);
        await db.SaveChangesAsync(cancellationToken);
        return industry.IndustryId;
    }

    private static string? NormalizeReferenceName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static (string BaseAssetSymbol, string? QuoteCurrency) ParseCryptoPair(string ticker)
    {
        var separatorIndex = ticker.LastIndexOf("-");
        if (separatorIndex <= 0 || separatorIndex >= ticker.Length - 1)
        {
            return (ticker, null);
        }

        return (
            ticker[..separatorIndex],
            ticker[(separatorIndex + 1)..]);
    }

    private static string NormalizeCryptoName(string name, string? quoteCurrency)
    {
        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(quoteCurrency))
        {
            return trimmedName;
        }

        var suffix = $" {quoteCurrency}";
        return trimmedName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? trimmedName[..^suffix.Length].Trim()
            : trimmedName;
    }
}
