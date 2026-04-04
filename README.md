# IPMS Backend

ASP.NET Core Web API backend for the Investment Portfolio Management System (IPMS), implemented against local SQL Server Express on this computer.

## Stack

- .NET 10 Web API
- Entity Framework Core with SQL Server
- Local database: `localhost\\SQLEXPRESS`
- Automatic database bootstrap and sample data seeding

## Run

```powershell
dotnet run --project .\src\Ipms.Api
```

The API starts on the URLs defined in [launchSettings.json](/C:/Users/LENOVO/OneDrive/Desktop/portfolio_management/src/Ipms.Api/Properties/launchSettings.json).

## SQL Server

The app uses this connection string by default:

```text
Server=localhost\SQLEXPRESS;Database=IPMS;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True
```

For SSMS, connect with:

- Server name: `localhost\SQLEXPRESS`
- Authentication: `Windows Authentication`
- Trust server certificate: enabled

## Seeded Data

The initializer creates sample:

- Users
- Portfolios
- Stock exchanges
- Stocks, ETFs, and cryptocurrencies
- Historical prices
- Transactions and live holdings

## Schema Notes

The schema is now closer to strict 3NF while keeping the system practical for portfolio analytics.

### Currency and Asset Normalization

The instrument model stores price currency explicitly instead of inferring it only from the exchange:

- `STOCKS.quote_currency`
- `ETFS.quote_currency`
- `CRYPTOCURRENCIES.quote_currency`

Crypto instruments are also normalized so the asset identity is separate from the quote currency:

- `FINANCIAL_INSTRUMENTS.name` stores the asset name only
- `CRYPTOCURRENCIES.base_asset_symbol` stores the base asset symbol
- `CRYPTOCURRENCIES.quote_currency` stores the pricing currency

Examples:

- `BTC-USD` -> `name = Bitcoin`, `base_asset_symbol = BTC`, `quote_currency = USD`
- `ETH-USD` -> `name = Ethereum`, `base_asset_symbol = ETH`, `quote_currency = USD`
- `0700.HK` -> `quote_currency = HKD`
- `NVDA` -> `quote_currency = USD`

### Stock Classification Normalization

Stock classification is normalized into reference tables:

- `SECTORS(sector_id, sector_name)`
- `INDUSTRIES(industry_id, industry_name, sector_id)`
- `STOCKS.industry_id`

This removes the old free-text `STOCKS.sector` and `STOCKS.industry` columns and avoids the transitive dependency problem where industry determines sector.

Examples:

- `AAPL` -> `Consumer Electronics` -> `Technology`
- `MSFT` -> `Software Infrastructure` -> `Technology`
- `0700.HK` -> `Internet Content & Information` -> `Communication Services`

### Transaction Normalization

`TRANSACTIONS.total_amount` has been removed from the table for a stricter textbook 3NF design.

- The database stores only `quantity` and `price_per_unit`
- API responses still return `totalAmount`
- `totalAmount` is now calculated in code as `quantity * price_per_unit`

This keeps the external API convenient without storing redundant transaction values in SQL Server.

### Remaining Pragmatic Denormalization

`PORTFOLIO_HOLDINGS.average_cost` is still stored as summary data. It is derivable from transaction history, but it is intentionally kept to make holdings, P/L, and dashboard queries much faster.

## Authentication

JWT bearer authentication is enabled for portfolio and analytics routes.

Sample seeded logins:

- `alice@example.com` / `Alice123!`
- `bob@example.com` / `Bob123!`

Public endpoints:

- `GET /api/health`
- `POST /api/auth/register`
- `POST /api/auth/login`
- `GET /api/instruments`
- `GET /api/stock-exchanges`

Protected endpoints require:

```text
Authorization: Bearer <token>
```

## Real Market Data

Yahoo Finance import is integrated for manual and scheduled refresh.

Protected market data endpoints:

- `GET /api/market-data/scheduler-status`
- `POST /api/market-data/import/by-ticker`
- `POST /api/market-data/import/instruments/{instrumentId}`
- `POST /api/market-data/import/all`

Example request:

```json
{
  "tickerSymbol": "NVDA",
  "createIfMissing": true,
  "range": "1mo",
  "interval": "1d"
}
```

This will:

- create the instrument if it does not exist
- fetch current price from Yahoo Finance
- insert or update rows in `HISTORICAL_PRICES`
- update `FINANCIAL_INSTRUMENTS.current_price`

## Scheduled Refresh

The backend includes an automatic background refresh job.

Default configuration is in [appsettings.json](/C:/Users/LENOVO/OneDrive/Desktop/portfolio_management/src/Ipms.Api/appsettings.json):

- enabled: `true`
- run on startup: `true`
- refresh interval: `3` seconds
- default range: `6mo`
- default interval: `1d`

## Market Universe Imports

The repository includes import scripts for larger demo datasets:

- [Import-StockcircleTop30.ps1](/C:/Users/LENOVO/OneDrive/Desktop/portfolio_management/scripts/Import-StockcircleTop30.ps1)
- [Import-DiverseUsHkDemoAccounts.ps1](/C:/Users/LENOVO/OneDrive/Desktop/portfolio_management/scripts/Import-DiverseUsHkDemoAccounts.ps1)
- [Import-BulkPortfolioAccounts.ps1](/C:/Users/LENOVO/OneDrive/Desktop/portfolio_management/scripts/Import-BulkPortfolioAccounts.ps1)
- [Import-MarketUniverse.ps1](/C:/Users/LENOVO/OneDrive/Desktop/portfolio_management/scripts/Import-MarketUniverse.ps1)

`Import-MarketUniverse.ps1` imports:

- the requested crypto ticker set
- the current S&P 500 constituent list
- the current top 100 Hong Kong stock list

Run it with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Import-MarketUniverse.ps1
```

After the latest import and normalization work, the database contains:

- explicit quote currencies for stocks, ETFs, and crypto
- `48` crypto instruments
- `100` Hong Kong stocks
- `503` S&P 500 constituents

## Stockcircle Demo Data

The repository includes [Import-StockcircleTop30.ps1](/C:/Users/LENOVO/OneDrive/Desktop/portfolio_management/scripts/Import-StockcircleTop30.ps1), which imports the current top 30 public profiles from Stockcircle's `best-investors` page into one demo login.

Demo login created by the script:

- `stockcircle.demo@example.com` / `Demo123!`

Run it with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Import-StockcircleTop30.ps1 -ResetExistingDemoPortfolios
```

Demo inspection queries are in [stockcircle-demo-queries.sql](/C:/Users/LENOVO/OneDrive/Desktop/portfolio_management/sql/stockcircle-demo-queries.sql).

## Useful Endpoints

- `POST /api/auth/login`
- `GET /api/auth/me`
- `GET /api/users/{userId}`
- `GET /api/portfolios`
- `GET /api/instruments`
- `GET /api/market-data/scheduler-status`
- `POST /api/market-data/import/by-ticker`
- `POST /api/market-data/import/all`
- `POST /api/portfolios/{portfolioId}/transactions`
- `GET /api/analytics/users/{userId}/portfolio-values`
- `GET /api/analytics/portfolios/{portfolioId}/allocation`
- `GET /api/analytics/portfolios/{portfolioId}/profit-loss`
- `GET /api/analytics/portfolios/{portfolioId}/sector-exposure`
- `GET /api/analytics/instruments/{instrumentId}/historical-performance`

## System Advantages

- Clear normalized core data model: users, portfolios, instruments, exchanges, sectors, industries, prices, and transactions are separated cleanly for easier maintenance.
- Better data quality: quote currencies and crypto base assets are stored explicitly, which avoids ambiguous names and fragile inference rules.
- Strong analytics support: holdings, allocation, profit/loss, sector exposure, and historical performance can be queried directly from the backend.
- Real market-data integration: Yahoo Finance import updates current prices and historical prices for stocks, ETFs, HK equities, and crypto.
- Scales well for demos and coursework: the project already supports seeded users, bulk demo users, market-universe imports, and large portfolio datasets in SQL Server.
- API-friendly design: the database stays normalized while the API still returns convenient computed values such as transaction totals and portfolio analytics.
- Easy local deployment: the system auto-creates the SQL Server database, seeds reference data, and runs locally on `localhost\\SQLEXPRESS`.
