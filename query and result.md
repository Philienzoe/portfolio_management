# Query and Result

This file records the SQL queries and the observed SSMS results from the screenshots captured on April 4, 2026 for the IPMS demonstration report.

## Query 1. Show All Accounts

Purpose: list every account stored in the system.

```sql
USE IPMS;

SELECT
    user_id,
    email,
    first_name,
    last_name,
    created_at
FROM USERS
ORDER BY email;
```

Observed result from screenshot:
- Returned `1,102` rows.
- Includes seeded accounts such as `alice@example.com` and `bob@example.com`.
- Includes bulk demo accounts such as `is5413.bulk0001@example.com`.

## Query 2. Show All Accounts with Portfolio Count

Purpose: show each account and the number of portfolios it owns.

```sql
USE IPMS;

SELECT
    u.user_id,
    u.email,
    CONCAT(u.first_name, ' ', u.last_name) AS full_name,
    COUNT(p.portfolio_id) AS portfolio_count
FROM USERS u
LEFT JOIN PORTFOLIOS p
    ON p.user_id = u.user_id
GROUP BY u.user_id, u.email, u.first_name, u.last_name
ORDER BY portfolio_count DESC, u.user_id;
```

Observed result from screenshot:
- Returned `1,102` rows.
- Demonstrates the bulk pattern where many `is5413.bulk...` users own up to `10` portfolios.

## Query 3. Show All Portfolios with Owner Details

Purpose: display every portfolio together with the owner and account currency.

```sql
USE IPMS;

SELECT
    p.portfolio_id,
    p.portfolio_name,
    p.description,
    p.currency,
    u.email,
    CONCAT(u.first_name, ' ', u.last_name) AS owner_name,
    p.created_at
FROM PORTFOLIOS p
JOIN USERS u
    ON u.user_id = p.user_id
ORDER BY u.email, p.portfolio_name;
```

Observed result from screenshot:
- Returned `5,603` rows.
- Shows both seeded portfolios like `Alice Growth` and generated bulk demo portfolios like `Portfolio 01`.

## Query 4. Show All Holdings with Live Market Value

Purpose: list all holdings and calculate live market value using the latest stored price.

```sql
USE IPMS;

SELECT
    u.email,
    p.portfolio_id,
    p.portfolio_name,
    fi.ticker_symbol,
    fi.name,
    fi.instrument_type,
    ph.quantity,
    ph.average_cost,
    fi.current_price,
    ph.quantity * COALESCE(fi.current_price, 0) AS market_value,
    fi.last_updated
FROM PORTFOLIO_HOLDINGS ph
JOIN PORTFOLIOS p
    ON p.portfolio_id = ph.portfolio_id
JOIN USERS u
    ON u.user_id = p.user_id
JOIN FINANCIAL_INSTRUMENTS fi
    ON fi.instrument_id = ph.instrument_id
ORDER BY u.email, p.portfolio_name, market_value DESC;
```

Observed result from screenshot:
- Returned `28,207` rows.
- Demonstrates live valuation using `current_price` and `last_updated`.
- Example rows shown include `BTC-USD`, `ETH-USD`, `SPY`, `AAPL`, `MSFT`, and Hong Kong stocks such as `0700.HK`.

## Query 5. Show Transaction History with Computed Total Amount

Purpose: display transactions and compute total amount in the query for strict 3NF compatibility.

```sql
USE IPMS;

SELECT
    t.transaction_id,
    u.email,
    p.portfolio_name,
    fi.ticker_symbol,
    t.transaction_type,
    t.quantity,
    t.price_per_unit,
    CAST(t.quantity * t.price_per_unit AS DECIMAL(28,8)) AS total_amount,
    t.fees,
    t.transaction_date,
    t.notes
FROM TRANSACTIONS t
JOIN PORTFOLIOS p
    ON p.portfolio_id = t.portfolio_id
JOIN USERS u
    ON u.user_id = p.user_id
JOIN FINANCIAL_INSTRUMENTS fi
    ON fi.instrument_id = t.instrument_id
ORDER BY t.transaction_date DESC, t.transaction_id DESC;
```

Observed result from screenshot:
- Returned `28,211` rows.
- Confirms that `total_amount` is now derived in SQL instead of stored as a table column.

## Query 6. Show All Instruments with Explicit Currency

Purpose: show all instruments and the quote currency used for pricing.

```sql
USE IPMS;

SELECT
    fi.instrument_id,
    fi.ticker_symbol,
    fi.name,
    fi.instrument_type,
    COALESCE(s.quote_currency, e.quote_currency, c.quote_currency) AS quote_currency,
    c.base_asset_symbol,
    fi.current_price,
    fi.last_updated
FROM FINANCIAL_INSTRUMENTS fi
LEFT JOIN STOCKS s
    ON s.instrument_id = fi.instrument_id
LEFT JOIN ETFS e
    ON e.instrument_id = fi.instrument_id
LEFT JOIN CRYPTOCURRENCIES c
    ON c.instrument_id = fi.instrument_id
ORDER BY fi.instrument_type, fi.ticker_symbol;
```

Observed result from screenshot:
- Returned `761` rows.
- Demonstrates explicit quote currency normalization for stocks, ETFs, and crypto.
- Example crypto rows shown include `ADA-USD`, `BTC-USD`, `ETH-USD`, `LINK-USD`, and `XRP-USD`.

## Query 7. Show Total Value of Each Portfolio

Purpose: calculate current portfolio value using the latest market prices.

```sql
USE IPMS;

SELECT
    p.portfolio_id,
    p.portfolio_name,
    u.email,
    p.currency,
    SUM(ph.quantity * COALESCE(fi.current_price, 0)) AS total_value,
    MAX(fi.last_updated) AS latest_market_data_time
FROM PORTFOLIOS p
JOIN USERS u
    ON u.user_id = p.user_id
JOIN PORTFOLIO_HOLDINGS ph
    ON ph.portfolio_id = p.portfolio_id
JOIN FINANCIAL_INSTRUMENTS fi
    ON fi.instrument_id = ph.instrument_id
GROUP BY p.portfolio_id, p.portfolio_name, u.email, p.currency
ORDER BY total_value DESC;
```

Observed result from screenshot:
- Returned `5,603` rows.
- Shows the highest-value demo portfolios at the top.
- Demonstrates portfolio valuation and latest live market data timestamp together.

## Query 8. Show Asset Allocation for One Portfolio

Purpose: calculate allocation percentage by instrument type for a selected portfolio.

```sql
DECLARE @PortfolioId INT = 1;

WITH Allocation AS (
    SELECT
        fi.instrument_type,
        SUM(ph.quantity * COALESCE(fi.current_price, 0)) AS type_value
    FROM PORTFOLIO_HOLDINGS ph
    JOIN FINANCIAL_INSTRUMENTS fi
        ON fi.instrument_id = ph.instrument_id
    WHERE ph.portfolio_id = @PortfolioId
    GROUP BY fi.instrument_type
)
SELECT
    instrument_type,
    type_value,
    ROUND(type_value * 100.0 / NULLIF(SUM(type_value) OVER (), 0), 2) AS allocation_pct
FROM Allocation
ORDER BY type_value DESC;
```

Observed result from screenshot:
- Returned `2` rows for portfolio `1`.
- Portfolio `1` is split between `ETF` and `STOCK`.
- Screenshot shows approximately `57.27% ETF` and `42.73% STOCK`.

## Query 9. Demonstrate Real-Time Return for One Portfolio

Purpose: show cost basis, market value, unrealized P/L, return percentage, and live price timestamp.

```sql
DECLARE @PortfolioId INT = 1;

SELECT
    p.portfolio_name,
    fi.ticker_symbol,
    fi.name,
    ph.quantity,
    ph.average_cost,
    fi.current_price,
    ph.quantity * ph.average_cost AS cost_basis,
    ph.quantity * COALESCE(fi.current_price, 0) AS market_value,
    ph.quantity * (COALESCE(fi.current_price, 0) - ph.average_cost) AS unrealized_pnl,
    ROUND(
        ((COALESCE(fi.current_price, 0) - ph.average_cost) * 100.0) / NULLIF(ph.average_cost, 0),
        2
    ) AS return_pct,
    fi.last_updated AS live_price_time,
    SYSDATETIME() AS query_time
FROM PORTFOLIO_HOLDINGS ph
JOIN PORTFOLIOS p
    ON p.portfolio_id = ph.portfolio_id
JOIN FINANCIAL_INSTRUMENTS fi
    ON fi.instrument_id = ph.instrument_id
WHERE ph.portfolio_id = @PortfolioId
ORDER BY unrealized_pnl DESC;
```

Observed result from screenshot:
- Returned `3` rows for `Alice Growth`.
- Demonstrates real-time return calculation using:
  - `current_price`
  - `average_cost`
  - `unrealized_pnl`
  - `return_pct`
  - `live_price_time`
  - `query_time`
- Example results shown:
  - `SPY` with positive unrealized profit
  - `AAPL` with positive unrealized profit
  - `MSFT` with a negative return in the captured screenshot

## Query 10. Corrected Latest Price and Previous Close for One Ticker

Purpose: show the latest stored live price correctly from `FINANCIAL_INSTRUMENTS.current_price` and compare it with the previous daily close.

```sql
USE IPMS;

DECLARE @Ticker NVARCHAR(20) = 'BTC-USD';

WITH PriceBase AS (
    SELECT
        fi.instrument_id,
        fi.ticker_symbol,
        fi.name,
        fi.current_price,
        fi.last_updated,
        hp.price_date,
        hp.close_price,
        ROW_NUMBER() OVER (
            PARTITION BY fi.instrument_id
            ORDER BY hp.price_date DESC
        ) AS rn
    FROM FINANCIAL_INSTRUMENTS fi
    LEFT JOIN HISTORICAL_PRICES hp
        ON hp.instrument_id = fi.instrument_id
    WHERE fi.ticker_symbol = @Ticker
),
Resolved AS (
    SELECT
        MAX(ticker_symbol) AS ticker_symbol,
        MAX(name) AS name,
        MAX(current_price) AS current_price,
        MAX(last_updated) AS last_updated,
        MAX(CASE WHEN rn = 1 THEN price_date END) AS latest_price_date,
        MAX(CASE WHEN rn = 1 THEN close_price END) AS latest_daily_close,
        MAX(CASE WHEN rn = 2 THEN close_price END) AS previous_daily_close
    FROM PriceBase
)
SELECT
    ticker_symbol,
    name,
    current_price,
    CASE
        WHEN latest_price_date = CAST(last_updated AS date) THEN previous_daily_close
        ELSE latest_daily_close
    END AS previous_close,
    current_price - CASE
        WHEN latest_price_date = CAST(last_updated AS date) THEN previous_daily_close
        ELSE latest_daily_close
    END AS price_change,
    ROUND(
        (current_price - CASE
            WHEN latest_price_date = CAST(last_updated AS date) THEN previous_daily_close
            ELSE latest_daily_close
        END) * 100.0 /
        NULLIF(CASE
            WHEN latest_price_date = CAST(last_updated AS date) THEN previous_daily_close
            ELSE latest_daily_close
        END, 0),
        2
    ) AS percentage_change,
    last_updated,
    SYSDATETIME() AS query_time
FROM Resolved;
```

Observed verification after refresh:
- `BTC-USD current_price = 66912.7300`
- `last_updated = 2026-04-04 06:32:43`
- `previous_close = 66930.6797`
- `price_change = -17.9497`

Important note:
- The older live query was misleading because `HISTORICAL_PRICES` stores one row per day, not one row per minute.
- That means today’s row is updated during refresh, so using the latest `close_price` directly can accidentally compare the current price to the same day’s value.
- For the latest stored price, always use `FINANCIAL_INSTRUMENTS.current_price`.
- For the previous close, use the prior distinct trading date from `HISTORICAL_PRICES`.

## Query 11. True Minute-by-Minute Live Return History

Purpose: show real intraday minute snapshots using the new `INTRADAY_PRICES` table.

```sql
USE IPMS;

DECLARE @Ticker NVARCHAR(20) = 'BTC-USD';

WITH InstrumentData AS (
    SELECT instrument_id, ticker_symbol, name
    FROM FINANCIAL_INSTRUMENTS
    WHERE ticker_symbol = @Ticker
),
PreviousClose AS (
    SELECT TOP 1
        hp.close_price AS previous_close
    FROM HISTORICAL_PRICES hp
    JOIN InstrumentData id
        ON id.instrument_id = hp.instrument_id
    WHERE hp.price_date < CAST(SYSUTCDATETIME() AS date)
    ORDER BY hp.price_date DESC
)
SELECT
    id.ticker_symbol,
    id.name,
    ip.price_time_utc,
    ip.close_price AS live_price,
    pc.previous_close,
    ip.close_price - pc.previous_close AS price_change,
    ROUND(
        (ip.close_price - pc.previous_close) * 100.0 / NULLIF(pc.previous_close, 0),
        4
    ) AS percentage_change,
    ip.volume
FROM INTRADAY_PRICES ip
JOIN InstrumentData id
    ON id.instrument_id = ip.instrument_id
CROSS JOIN PreviousClose pc
WHERE ip.price_time_utc >= DATEADD(MINUTE, -30, SYSUTCDATETIME())
ORDER BY ip.price_time_utc ASC;
```

Verified result after implementation:
- `INTRADAY_PRICES` now stores minute snapshots for `BTC-USD`.
- A manual `1d / 1m` Yahoo refresh inserted `356` intraday points.
- Example recent rows:
  - `2026-04-04 06:35:00 -> 66930.7031`
  - `2026-04-04 06:36:00 -> 66928.2891`
  - `2026-04-04 06:37:00 -> 66922.3906`
  - `2026-04-04 06:39:00 -> 66922.8400`
- The API endpoint `/api/analytics/instruments/5/intraday-returns?minutes=30` now returns the same minute-by-minute history with computed return values.

## Report Note

These query blocks match the screenshots captured in SSMS and can be used directly in the report appendix under a section such as `SQL Query Demonstrations` or `System Validation Queries`.
