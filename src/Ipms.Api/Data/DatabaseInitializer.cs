using Ipms.Api.Models;
using Ipms.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace Ipms.Api.Data;

public sealed class DatabaseInitializer(
    IpmsDbContext dbContext,
    IConfiguration configuration,
    ITransactionService transactionService,
    IPasswordHasher<User> passwordHasher)
{
    private readonly IpmsDbContext _dbContext = dbContext;
    private readonly IConfiguration _configuration = configuration;
    private readonly ITransactionService _transactionService = transactionService;
    private readonly IPasswordHasher<User> _passwordHasher = passwordHasher;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseExistsAsync(cancellationToken);
        await _dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureSchemaUpdatesAsync(cancellationToken);
        await RepairSeedUserPasswordsAsync(cancellationToken);

        if (await _dbContext.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        await SeedReferenceDataAsync(cancellationToken);
    }

    private async Task EnsureDatabaseExistsAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString("IpmsDatabase")
            ?? throw new InvalidOperationException("Missing SQL Server connection string.");

        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("The SQL Server connection string must include a database name.");
        }

        builder.InitialCatalog = "master";

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var safeDatabaseName = databaseName.Replace("]", "]]", StringComparison.Ordinal);
        var literalDatabaseName = databaseName.Replace("'", "''", StringComparison.Ordinal);
        var commandText = $"""
IF DB_ID(N'{literalDatabaseName}') IS NULL
BEGIN
    EXEC('CREATE DATABASE [{safeDatabaseName}]');
END
""";

        await using var command = new SqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureSchemaUpdatesAsync(CancellationToken cancellationToken)
    {
        const string ensureSchemaObjectsCommand = """
IF COL_LENGTH('STOCKS', 'quote_currency') IS NULL
BEGIN
    ALTER TABLE STOCKS ADD quote_currency NVARCHAR(10) NULL;
END;

IF COL_LENGTH('STOCKS', 'industry_id') IS NULL
BEGIN
    ALTER TABLE STOCKS ADD industry_id INT NULL;
END;

IF COL_LENGTH('ETFS', 'quote_currency') IS NULL
BEGIN
    ALTER TABLE ETFS ADD quote_currency NVARCHAR(10) NULL;
END;

IF COL_LENGTH('CRYPTOCURRENCIES', 'base_asset_symbol') IS NULL
BEGIN
    ALTER TABLE CRYPTOCURRENCIES ADD base_asset_symbol NVARCHAR(40) NULL;
END;

IF COL_LENGTH('CRYPTOCURRENCIES', 'quote_currency') IS NULL
BEGIN
    ALTER TABLE CRYPTOCURRENCIES ADD quote_currency NVARCHAR(10) NULL;
END;

IF OBJECT_ID('SECTORS', 'U') IS NULL
BEGIN
    CREATE TABLE SECTORS
    (
        sector_id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        sector_name NVARCHAR(100) NOT NULL
    );
END;

IF OBJECT_ID('INDUSTRIES', 'U') IS NULL
BEGIN
    CREATE TABLE INDUSTRIES
    (
        industry_id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        industry_name NVARCHAR(100) NOT NULL,
        sector_id INT NOT NULL
    );
END;

IF OBJECT_ID('INTRADAY_PRICES', 'U') IS NULL
BEGIN
    CREATE TABLE INTRADAY_PRICES
    (
        intraday_price_id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        instrument_id INT NOT NULL,
        price_time_utc DATETIME2(0) NOT NULL,
        open_price DECIMAL(19,4) NOT NULL,
        high_price DECIMAL(19,4) NOT NULL,
        low_price DECIMAL(19,4) NOT NULL,
        close_price DECIMAL(19,4) NOT NULL,
        volume BIGINT NULL
    );
END;

IF OBJECT_ID('REALTIME_PRICE_SNAPSHOTS', 'U') IS NULL
BEGIN
    CREATE TABLE REALTIME_PRICE_SNAPSHOTS
    (
        realtime_price_snapshot_id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        instrument_id INT NOT NULL,
        snapshot_time_utc DATETIME2(0) NOT NULL,
        source_time_utc DATETIME2(0) NULL,
        price DECIMAL(19,4) NOT NULL,
        volume BIGINT NULL
    );
END;
""";

        const string backfillInstrumentCurrenciesCommand = """
UPDATE s
SET s.quote_currency = CASE
    WHEN sx.mic_code = 'XHKG' THEN 'HKD'
    WHEN sx.mic_code IN ('XNAS', 'XNYS') THEN 'USD'
    WHEN fi.ticker_symbol LIKE '____.HK' THEN 'HKD'
    ELSE 'USD'
END
FROM STOCKS s
JOIN FINANCIAL_INSTRUMENTS fi
    ON fi.instrument_id = s.instrument_id
LEFT JOIN STOCK_EXCHANGES sx
    ON sx.exchange_id = s.exchange_id
WHERE s.quote_currency IS NULL;

UPDATE e
SET e.quote_currency = CASE
    WHEN sx.mic_code = 'XHKG' THEN 'HKD'
    WHEN sx.mic_code IN ('XNAS', 'XNYS') THEN 'USD'
    WHEN fi.ticker_symbol LIKE '____.HK' THEN 'HKD'
    ELSE 'USD'
END
FROM ETFS e
JOIN FINANCIAL_INSTRUMENTS fi
    ON fi.instrument_id = e.instrument_id
LEFT JOIN STOCK_EXCHANGES sx
    ON sx.exchange_id = e.exchange_id
WHERE e.quote_currency IS NULL;
""";

        const string ensureReferenceConstraintsCommand = """
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_SECTORS_sector_name'
      AND object_id = OBJECT_ID('SECTORS'))
BEGIN
    CREATE UNIQUE INDEX UX_SECTORS_sector_name
        ON SECTORS(sector_name);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_INDUSTRIES_industry_name'
      AND object_id = OBJECT_ID('INDUSTRIES'))
BEGIN
    CREATE UNIQUE INDEX UX_INDUSTRIES_industry_name
        ON INDUSTRIES(industry_name);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_STOCKS_industry_id'
      AND object_id = OBJECT_ID('STOCKS'))
BEGIN
    CREATE INDEX IX_STOCKS_industry_id
        ON STOCKS(industry_id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_INDUSTRIES_SECTORS')
BEGIN
    ALTER TABLE INDUSTRIES
        ADD CONSTRAINT FK_INDUSTRIES_SECTORS
        FOREIGN KEY (sector_id) REFERENCES SECTORS(sector_id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_STOCKS_INDUSTRIES')
BEGIN
    ALTER TABLE STOCKS
        ADD CONSTRAINT FK_STOCKS_INDUSTRIES
        FOREIGN KEY (industry_id) REFERENCES INDUSTRIES(industry_id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_INTRADAY_PRICES_instrument_time'
      AND object_id = OBJECT_ID('INTRADAY_PRICES'))
BEGIN
    CREATE UNIQUE INDEX UX_INTRADAY_PRICES_instrument_time
        ON INTRADAY_PRICES(instrument_id, price_time_utc);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_INTRADAY_PRICES_FINANCIAL_INSTRUMENTS')
BEGIN
    ALTER TABLE INTRADAY_PRICES
        ADD CONSTRAINT FK_INTRADAY_PRICES_FINANCIAL_INSTRUMENTS
        FOREIGN KEY (instrument_id) REFERENCES FINANCIAL_INSTRUMENTS(instrument_id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_REALTIME_PRICE_SNAPSHOTS_instrument_time'
      AND object_id = OBJECT_ID('REALTIME_PRICE_SNAPSHOTS'))
BEGIN
    CREATE UNIQUE INDEX UX_REALTIME_PRICE_SNAPSHOTS_instrument_time
        ON REALTIME_PRICE_SNAPSHOTS(instrument_id, snapshot_time_utc);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_REALTIME_PRICE_SNAPSHOTS_FINANCIAL_INSTRUMENTS')
BEGIN
    ALTER TABLE REALTIME_PRICE_SNAPSHOTS
        ADD CONSTRAINT FK_REALTIME_PRICE_SNAPSHOTS_FINANCIAL_INSTRUMENTS
        FOREIGN KEY (instrument_id) REFERENCES FINANCIAL_INSTRUMENTS(instrument_id);
END;
""";

        const string backfillStockClassificationCommand = """
IF NOT EXISTS (SELECT 1 FROM SECTORS WHERE sector_name = 'Unclassified')
BEGIN
    INSERT INTO SECTORS (sector_name) VALUES ('Unclassified');
END;

DECLARE @hasLegacySectorColumn BIT = CASE WHEN COL_LENGTH('STOCKS', 'sector') IS NOT NULL THEN 1 ELSE 0 END;
DECLARE @hasLegacyIndustryColumn BIT = CASE WHEN COL_LENGTH('STOCKS', 'industry') IS NOT NULL THEN 1 ELSE 0 END;

IF @hasLegacySectorColumn = 1 OR @hasLegacyIndustryColumn = 1
BEGIN
    DECLARE @sql NVARCHAR(MAX) = N'
    ;WITH LegacyClassification AS
    (
        SELECT
            instrument_id,
            ' + CASE
                    WHEN @hasLegacySectorColumn = 1
                        THEN N'NULLIF(LTRIM(RTRIM([sector])), '''''')'
                    ELSE N'CAST(NULL AS NVARCHAR(100))'
                END + N' AS raw_sector,
            ' + CASE
                    WHEN @hasLegacyIndustryColumn = 1
                        THEN N'NULLIF(LTRIM(RTRIM([industry])), '''''')'
                    ELSE N'CAST(NULL AS NVARCHAR(100))'
                END + N' AS raw_industry
        FROM STOCKS
    ),
    CleanedClassification AS
    (
        SELECT
            instrument_id,
            CASE
                WHEN raw_sector IS NOT NULL
                 AND UPPER(raw_sector) IN (''NYSE'', ''NYSE AMERICAN'', ''NYSEARCA'', ''NYSE ARCA'', ''NASDAQ'', ''NASDAQGS'', ''NASDAQGM'', ''NASDAQCM'', ''HKSE'', ''HKEX'', ''CBOE US'', ''NEW YORK STOCK EXCHANGE'', ''HONG KONG STOCK EXCHANGE'')
                    THEN NULL
                ELSE raw_sector
            END AS sector_name,
            CASE
                WHEN raw_industry IS NOT NULL
                 AND UPPER(raw_industry) IN (''NYSE'', ''NYSE AMERICAN'', ''NYSEARCA'', ''NYSE ARCA'', ''NASDAQ'', ''NASDAQGS'', ''NASDAQGM'', ''NASDAQCM'', ''HKSE'', ''HKEX'', ''CBOE US'', ''NEW YORK STOCK EXCHANGE'', ''HONG KONG STOCK EXCHANGE'')
                    THEN NULL
                ELSE raw_industry
            END AS industry_name
        FROM LegacyClassification
    ),
    SourceSectors AS
    (
        SELECT DISTINCT
            COALESCE(sector_name, ''Unclassified'') AS sector_name
        FROM CleanedClassification
        WHERE sector_name IS NOT NULL
           OR industry_name IS NOT NULL
    )
    INSERT INTO SECTORS (sector_name)
    SELECT source.sector_name
    FROM SourceSectors source
    WHERE NOT EXISTS (
        SELECT 1
        FROM SECTORS target
        WHERE target.sector_name = source.sector_name);

    ;WITH SourceIndustries AS
    (
        SELECT DISTINCT
            COALESCE(
                industry_name,
                CASE
                    WHEN sector_name IS NOT NULL THEN CONCAT(sector_name, '' General'')
                    ELSE NULL
                END) AS industry_name,
            COALESCE(sector_name, ''Unclassified'') AS sector_name
        FROM CleanedClassification
        WHERE industry_name IS NOT NULL
           OR sector_name IS NOT NULL
    )
    INSERT INTO INDUSTRIES (industry_name, sector_id)
    SELECT source.industry_name, sector.sector_id
    FROM SourceIndustries source
    JOIN SECTORS sector
        ON sector.sector_name = source.sector_name
    WHERE source.industry_name IS NOT NULL
      AND NOT EXISTS (
        SELECT 1
        FROM INDUSTRIES target
        WHERE target.industry_name = source.industry_name);

    ;WITH CleanedClassification AS
    (
        SELECT
            instrument_id,
            CASE
                WHEN ' + CASE
                            WHEN @hasLegacySectorColumn = 1
                                THEN N'NULLIF(LTRIM(RTRIM([sector])), '''''')'
                            ELSE N'CAST(NULL AS NVARCHAR(100))'
                        END + N' IS NOT NULL
                 AND UPPER(' + CASE
                                    WHEN @hasLegacySectorColumn = 1
                                        THEN N'NULLIF(LTRIM(RTRIM([sector])), '''''')'
                                    ELSE N'CAST(NULL AS NVARCHAR(100))'
                                END + N') IN (''NYSE'', ''NYSE AMERICAN'', ''NYSEARCA'', ''NYSE ARCA'', ''NASDAQ'', ''NASDAQGS'', ''NASDAQGM'', ''NASDAQCM'', ''HKSE'', ''HKEX'', ''CBOE US'', ''NEW YORK STOCK EXCHANGE'', ''HONG KONG STOCK EXCHANGE'')
                    THEN NULL
                ELSE ' + CASE
                            WHEN @hasLegacySectorColumn = 1
                                THEN N'NULLIF(LTRIM(RTRIM([sector])), '''''')'
                            ELSE N'CAST(NULL AS NVARCHAR(100))'
                        END + N'
            END AS sector_name,
            CASE
                WHEN ' + CASE
                            WHEN @hasLegacyIndustryColumn = 1
                                THEN N'NULLIF(LTRIM(RTRIM([industry])), '''''')'
                            ELSE N'CAST(NULL AS NVARCHAR(100))'
                        END + N' IS NOT NULL
                 AND UPPER(' + CASE
                                    WHEN @hasLegacyIndustryColumn = 1
                                        THEN N'NULLIF(LTRIM(RTRIM([industry])), '''''')'
                                    ELSE N'CAST(NULL AS NVARCHAR(100))'
                                END + N') IN (''NYSE'', ''NYSE AMERICAN'', ''NYSEARCA'', ''NYSE ARCA'', ''NASDAQ'', ''NASDAQGS'', ''NASDAQGM'', ''NASDAQCM'', ''HKSE'', ''HKEX'', ''CBOE US'', ''NEW YORK STOCK EXCHANGE'', ''HONG KONG STOCK EXCHANGE'')
                    THEN NULL
                ELSE ' + CASE
                            WHEN @hasLegacyIndustryColumn = 1
                                THEN N'NULLIF(LTRIM(RTRIM([industry])), '''''')'
                            ELSE N'CAST(NULL AS NVARCHAR(100))'
                        END + N'
            END AS industry_name
        FROM STOCKS
    )
    UPDATE stock
    SET stock.industry_id = industry.industry_id
    FROM STOCKS stock
    JOIN CleanedClassification classification
        ON classification.instrument_id = stock.instrument_id
    JOIN INDUSTRIES industry
        ON industry.industry_name = COALESCE(
            classification.industry_name,
            CASE
                WHEN classification.sector_name IS NOT NULL THEN CONCAT(classification.sector_name, '' General'')
                ELSE NULL
            END)
    WHERE stock.industry_id IS NULL
      AND (
          classification.industry_name IS NOT NULL
          OR classification.sector_name IS NOT NULL);';

    EXEC sp_executesql @sql;
END;
""";

        const string cleanInvalidStockClassificationCommand = """
;WITH InvalidIndustries AS
(
    SELECT industry_id
    FROM INDUSTRIES
    WHERE UPPER(LTRIM(RTRIM(industry_name))) IN (
        'NYSE',
        'NYSE AMERICAN',
        'NYSEARCA',
        'NYSE ARCA',
        'NASDAQ',
        'NASDAQGS',
        'NASDAQGM',
        'NASDAQCM',
        'HKSE',
        'HKEX',
        'CBOE US',
        'NEW YORK STOCK EXCHANGE',
        'HONG KONG STOCK EXCHANGE')
)
UPDATE stock
SET stock.industry_id = NULL
FROM STOCKS stock
JOIN InvalidIndustries invalidIndustry
    ON invalidIndustry.industry_id = stock.industry_id;

DELETE industry
FROM INDUSTRIES industry
LEFT JOIN STOCKS stock
    ON stock.industry_id = industry.industry_id
WHERE stock.instrument_id IS NULL
  AND UPPER(LTRIM(RTRIM(industry.industry_name))) IN (
        'NYSE',
        'NYSE AMERICAN',
        'NYSEARCA',
        'NYSE ARCA',
        'NASDAQ',
        'NASDAQGS',
        'NASDAQGM',
        'NASDAQCM',
        'HKSE',
        'HKEX',
        'CBOE US',
        'NEW YORK STOCK EXCHANGE',
        'HONG KONG STOCK EXCHANGE');
""";

        const string backfillCryptoCommand = """
UPDATE c
SET
    base_asset_symbol = CASE
        WHEN CHARINDEX('-', fi.ticker_symbol) > 0 THEN LEFT(fi.ticker_symbol, CHARINDEX('-', fi.ticker_symbol) - 1)
        ELSE fi.ticker_symbol
    END,
    quote_currency = CASE
        WHEN CHARINDEX('-', fi.ticker_symbol) > 0 THEN SUBSTRING(fi.ticker_symbol, CHARINDEX('-', fi.ticker_symbol) + 1, LEN(fi.ticker_symbol))
        ELSE c.quote_currency
    END
FROM CRYPTOCURRENCIES c
JOIN FINANCIAL_INSTRUMENTS fi
    ON fi.instrument_id = c.instrument_id
WHERE c.base_asset_symbol IS NULL
   OR c.quote_currency IS NULL;
""";

        const string normalizeCryptoNamesCommand = """
UPDATE fi
SET fi.name = LEFT(fi.name, LEN(fi.name) - LEN(c.quote_currency) - 1)
FROM FINANCIAL_INSTRUMENTS fi
JOIN CRYPTOCURRENCIES c
    ON c.instrument_id = fi.instrument_id
WHERE fi.instrument_type = 'CRYPTO'
  AND c.quote_currency IS NOT NULL
  AND fi.name LIKE '% ' + c.quote_currency;
""";

        const string dropLegacyStockColumnsCommand = """
IF COL_LENGTH('STOCKS', 'sector') IS NOT NULL
BEGIN
    ALTER TABLE STOCKS DROP COLUMN [sector];
END;

IF COL_LENGTH('STOCKS', 'industry') IS NOT NULL
BEGIN
    ALTER TABLE STOCKS DROP COLUMN [industry];
END;
""";

        const string dropTransactionTotalAmountCommand = """
IF COL_LENGTH('TRANSACTIONS', 'total_amount') IS NOT NULL
BEGIN
    ALTER TABLE TRANSACTIONS DROP COLUMN [total_amount];
END;
""";

        await _dbContext.Database.ExecuteSqlRawAsync(ensureSchemaObjectsCommand, cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(backfillInstrumentCurrenciesCommand, cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(ensureReferenceConstraintsCommand, cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(backfillStockClassificationCommand, cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(cleanInvalidStockClassificationCommand, cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(backfillCryptoCommand, cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(normalizeCryptoNamesCommand, cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(dropTransactionTotalAmountCommand, cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(dropLegacyStockColumnsCommand, cancellationToken);
    }

    private async Task SeedReferenceDataAsync(CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddMonths(-5));

        var nyse = new StockExchange
        {
            MicCode = "XNYS",
            Name = "New York Stock Exchange",
            Country = "USA",
            City = "New York",
            Timezone = "EST"
        };

        var nasdaq = new StockExchange
        {
            MicCode = "XNAS",
            Name = "NASDAQ",
            Country = "USA",
            City = "New York",
            Timezone = "EST"
        };

        var hkex = new StockExchange
        {
            MicCode = "XHKG",
            Name = "Hong Kong Stock Exchange",
            Country = "Hong Kong",
            City = "Hong Kong",
            Timezone = "HKT"
        };

        var alice = new User
        {
            Email = "alice@example.com",
            PasswordHash = string.Empty,
            FirstName = "Alice",
            LastName = "Chen",
            CreatedAt = utcNow.AddMonths(-4)
        };

        var bob = new User
        {
            Email = "bob@example.com",
            PasswordHash = string.Empty,
            FirstName = "Bob",
            LastName = "Wong",
            CreatedAt = utcNow.AddMonths(-3)
        };

        alice.PasswordHash = _passwordHasher.HashPassword(alice, "Alice123!");
        bob.PasswordHash = _passwordHasher.HashPassword(bob, "Bob123!");

        var aliceGrowth = new Portfolio
        {
            User = alice,
            PortfolioName = "Alice Growth",
            Description = "US growth and broad market exposure.",
            Currency = "USD",
            CreatedAt = utcNow.AddMonths(-4)
        };

        var aliceCrypto = new Portfolio
        {
            User = alice,
            PortfolioName = "Alice Crypto",
            Description = "Core crypto allocation with large-cap assets.",
            Currency = "USD",
            CreatedAt = utcNow.AddMonths(-3)
        };

        var bobAsia = new Portfolio
        {
            User = bob,
            PortfolioName = "Bob Asia",
            Description = "Hong Kong market and regional diversification.",
            Currency = "HKD",
            CreatedAt = utcNow.AddMonths(-2)
        };

        var technologySector = new Sector
        {
            SectorName = "Technology"
        };

        var communicationServicesSector = new Sector
        {
            SectorName = "Communication Services"
        };

        var consumerElectronicsIndustry = new Industry
        {
            IndustryName = "Consumer Electronics",
            Sector = technologySector
        };

        var softwareInfrastructureIndustry = new Industry
        {
            IndustryName = "Software Infrastructure",
            Sector = technologySector
        };

        var internetContentIndustry = new Industry
        {
            IndustryName = "Internet Content & Information",
            Sector = communicationServicesSector
        };

        var aapl = new FinancialInstrument
        {
            TickerSymbol = "AAPL",
            InstrumentType = InstrumentTypes.Stock,
            Name = "Apple Inc.",
            CurrentPrice = 192.4500m,
            LastUpdated = utcNow,
            Stock = new Stock
            {
                Industry = consumerElectronicsIndustry,
                QuoteCurrency = "USD",
                MarketCap = 2900000000000m,
                PeRatio = 29.4800m,
                DividendYield = 0.5400m,
                Exchange = nasdaq
            }
        };

        var msft = new FinancialInstrument
        {
            TickerSymbol = "MSFT",
            InstrumentType = InstrumentTypes.Stock,
            Name = "Microsoft Corporation",
            CurrentPrice = 423.1500m,
            LastUpdated = utcNow,
            Stock = new Stock
            {
                Industry = softwareInfrastructureIndustry,
                QuoteCurrency = "USD",
                MarketCap = 3150000000000m,
                PeRatio = 34.7200m,
                DividendYield = 0.6800m,
                Exchange = nasdaq
            }
        };

        var spy = new FinancialInstrument
        {
            TickerSymbol = "SPY",
            InstrumentType = InstrumentTypes.Etf,
            Name = "SPDR S&P 500 ETF Trust",
            CurrentPrice = 521.3400m,
            LastUpdated = utcNow,
            Etf = new Etf
            {
                AssetClass = "Equity",
                ExpenseRatio = 0.0945m,
                Issuer = "State Street",
                TrackingIndex = "S&P 500",
                QuoteCurrency = "USD",
                Exchange = nyse
            }
        };

        var qqq = new FinancialInstrument
        {
            TickerSymbol = "QQQ",
            InstrumentType = InstrumentTypes.Etf,
            Name = "Invesco QQQ Trust",
            CurrentPrice = 441.1200m,
            LastUpdated = utcNow,
            Etf = new Etf
            {
                AssetClass = "Equity",
                ExpenseRatio = 0.2000m,
                Issuer = "Invesco",
                TrackingIndex = "NASDAQ-100",
                QuoteCurrency = "USD",
                Exchange = nasdaq
            }
        };

        var btc = new FinancialInstrument
        {
            TickerSymbol = "BTC-USD",
            InstrumentType = InstrumentTypes.Crypto,
            Name = "Bitcoin",
            CurrentPrice = 69000.1234m,
            LastUpdated = utcNow,
            Cryptocurrency = new Cryptocurrency
            {
                BaseAssetSymbol = "BTC",
                QuoteCurrency = "USD",
                Blockchain = "Bitcoin",
                HashingAlgorithm = "SHA-256",
                MaxSupply = 21000000m,
                CirculatingSupply = 19685000m
            }
        };

        var eth = new FinancialInstrument
        {
            TickerSymbol = "ETH-USD",
            InstrumentType = InstrumentTypes.Crypto,
            Name = "Ethereum",
            CurrentPrice = 3500.5678m,
            LastUpdated = utcNow,
            Cryptocurrency = new Cryptocurrency
            {
                BaseAssetSymbol = "ETH",
                QuoteCurrency = "USD",
                Blockchain = "Ethereum",
                HashingAlgorithm = "Ethash",
                CirculatingSupply = 120150000m
            }
        };

        var tencent = new FinancialInstrument
        {
            TickerSymbol = "0700.HK",
            InstrumentType = InstrumentTypes.Stock,
            Name = "Tencent Holdings Ltd.",
            CurrentPrice = 321.5000m,
            LastUpdated = utcNow,
            Stock = new Stock
            {
                Industry = internetContentIndustry,
                QuoteCurrency = "HKD",
                MarketCap = 2980000000000m,
                PeRatio = 18.6500m,
                DividendYield = 0.9100m,
                Exchange = hkex
            }
        };

        var trackerFund = new FinancialInstrument
        {
            TickerSymbol = "2800.HK",
            InstrumentType = InstrumentTypes.Etf,
            Name = "Tracker Fund of Hong Kong",
            CurrentPrice = 17.5500m,
            LastUpdated = utcNow,
            Etf = new Etf
            {
                AssetClass = "Equity",
                ExpenseRatio = 0.0990m,
                Issuer = "State Street Global Advisors Asia",
                TrackingIndex = "Hang Seng Index",
                QuoteCurrency = "HKD",
                Exchange = hkex
            }
        };

        _dbContext.StockExchanges.AddRange(nyse, nasdaq, hkex);
        _dbContext.Users.AddRange(alice, bob);
        _dbContext.Portfolios.AddRange(aliceGrowth, aliceCrypto, bobAsia);
        _dbContext.FinancialInstruments.AddRange(aapl, msft, spy, qqq, btc, eth, tencent, trackerFund);
        _dbContext.HistoricalPrices.AddRange(
            BuildMonthlyPrices(aapl, startDate, [171.4200m, 176.2000m, 182.0500m, 186.7400m, 189.3100m, 192.4500m], 91000000)
                .Concat(BuildMonthlyPrices(msft, startDate, [383.1500m, 390.8000m, 401.1000m, 408.5500m, 417.2000m, 423.1500m], 28000000))
                .Concat(BuildMonthlyPrices(spy, startDate, [489.2000m, 496.7000m, 503.4500m, 510.2200m, 516.4800m, 521.3400m], 72000000))
                .Concat(BuildMonthlyPrices(qqq, startDate, [411.6300m, 418.5000m, 424.7800m, 431.9000m, 437.2500m, 441.1200m], 41000000))
                .Concat(BuildMonthlyPrices(btc, startDate, [60250.2300m, 61890.5800m, 63440.9600m, 65280.3300m, 67110.8700m, 69000.1234m], 1250000))
                .Concat(BuildMonthlyPrices(eth, startDate, [3021.4400m, 3112.8600m, 3220.5500m, 3335.7800m, 3431.4200m, 3500.5678m], 5400000))
                .Concat(BuildMonthlyPrices(tencent, startDate, [295.1000m, 301.2500m, 307.8000m, 312.9500m, 318.4000m, 321.5000m], 21000000))
                .Concat(BuildMonthlyPrices(trackerFund, startDate, [16.7200m, 16.8800m, 17.0100m, 17.1800m, 17.3400m, 17.5500m], 65000000)));

        await _dbContext.SaveChangesAsync(cancellationToken);

        var seedTransactions = new[]
        {
            new RecordTransactionCommand(aliceGrowth.PortfolioId, aapl.InstrumentId, TransactionKind.Buy, 10m, 170.0000m, utcNow.AddMonths(-4).AddDays(3), 4.95m, "Initial Apple position"),
            new RecordTransactionCommand(aliceGrowth.PortfolioId, msft.InstrumentId, TransactionKind.Buy, 5m, 390.0000m, utcNow.AddMonths(-3).AddDays(8), 4.95m, "Software allocation"),
            new RecordTransactionCommand(aliceGrowth.PortfolioId, spy.InstrumentId, TransactionKind.Buy, 8m, 500.0000m, utcNow.AddMonths(-2).AddDays(12), 4.95m, "Broad market ETF"),
            new RecordTransactionCommand(aliceGrowth.PortfolioId, aapl.InstrumentId, TransactionKind.Sell, 2m, 188.0000m, utcNow.AddMonths(-1).AddDays(10), 4.95m, "Trimmed position"),
            new RecordTransactionCommand(aliceGrowth.PortfolioId, msft.InstrumentId, TransactionKind.Dividend, 5m, 0.7500m, utcNow.AddMonths(-1).AddDays(2), 0m, "Quarterly dividend"),
            new RecordTransactionCommand(aliceCrypto.PortfolioId, btc.InstrumentId, TransactionKind.Buy, 0.35m, 62000.0000m, utcNow.AddMonths(-3).AddDays(6), 8.50m, "Initial BTC allocation"),
            new RecordTransactionCommand(aliceCrypto.PortfolioId, eth.InstrumentId, TransactionKind.Buy, 2.50m, 3100.0000m, utcNow.AddMonths(-2).AddDays(5), 5.25m, "Initial ETH allocation"),
            new RecordTransactionCommand(aliceCrypto.PortfolioId, btc.InstrumentId, TransactionKind.Buy, 0.10m, 65000.0000m, utcNow.AddMonths(-1).AddDays(1), 4.25m, "Added on pullback"),
            new RecordTransactionCommand(bobAsia.PortfolioId, tencent.InstrumentId, TransactionKind.Buy, 50m, 305.0000m, utcNow.AddMonths(-2).AddDays(7), 18.00m, "Core HK tech holding"),
            new RecordTransactionCommand(bobAsia.PortfolioId, trackerFund.InstrumentId, TransactionKind.Buy, 1000m, 16.9000m, utcNow.AddMonths(-2).AddDays(9), 30.00m, "Hang Seng index exposure"),
            new RecordTransactionCommand(bobAsia.PortfolioId, trackerFund.InstrumentId, TransactionKind.Sell, 200m, 17.3000m, utcNow.AddMonths(-1).AddDays(4), 20.00m, "Rebalanced ETF weight")
        };

        foreach (var command in seedTransactions)
        {
            var result = await _transactionService.RecordTransactionAsync(command, cancellationToken);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Failed to seed transaction data: {result.ErrorMessage}");
            }
        }
    }

    private async Task RepairSeedUserPasswordsAsync(CancellationToken cancellationToken)
    {
        var seedPasswords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alice@example.com"] = "Alice123!",
            ["bob@example.com"] = "Bob123!"
        };

        var users = await _dbContext.Users
            .Where(user => seedPasswords.Keys.Contains(user.Email))
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var user in users)
        {
            var defaultPassword = seedPasswords[user.Email];
            PasswordVerificationResult verification;
            try
            {
                verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, defaultPassword);
            }
            catch (FormatException)
            {
                verification = PasswordVerificationResult.Failed;
            }

            if (verification == PasswordVerificationResult.Failed)
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, defaultPassword);
                changed = true;
            }
        }

        if (changed)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static IEnumerable<HistoricalPrice> BuildMonthlyPrices(
        FinancialInstrument instrument,
        DateOnly startDate,
        IReadOnlyList<decimal> closePrices,
        long baseVolume)
    {
        for (var index = 0; index < closePrices.Count; index++)
        {
            var close = closePrices[index];
            yield return new HistoricalPrice
            {
                Instrument = instrument,
                PriceDate = startDate.AddMonths(index),
                OpenPrice = Math.Round(close * 0.985m, 4),
                HighPrice = Math.Round(close * 1.025m, 4),
                LowPrice = Math.Round(close * 0.975m, 4),
                ClosePrice = close,
                AdjustedClose = close,
                Volume = baseVolume + (index * 250000)
            };
        }
    }
}
