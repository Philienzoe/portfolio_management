# IPMS Query Results

Generated on `2026-04-04` against SQL Server `localhost\SQLEXPRESS`, database `IPMS`.

## Query 1. Number of Accounts

Purpose: count the total number of user accounts stored in the system.

```sql
USE IPMS;
GO

SELECT COUNT(*) AS total_accounts
FROM USERS;
```

Observed result:

- Returned `1102`.

## Query 2. Number of Portfolios

Purpose: count the total number of portfolios across all users.

```sql
USE IPMS;
GO

SELECT COUNT(*) AS total_portfolios
FROM PORTFOLIOS;
```

Observed result:

- Returned `5603`.

## Query 3. Instrument Count by Type

Purpose: show how many stocks, ETFs, and cryptocurrencies are currently tracked.

```sql
USE IPMS;
GO

SELECT
    instrument_type,
    COUNT(*) AS instrument_count
FROM FINANCIAL_INSTRUMENTS
GROUP BY instrument_type
ORDER BY instrument_type;
```

Observed result:

| instrument_type | instrument_count |
| --- | ---: |
| CRYPTO | 48 |
| ETF | 6 |
| STOCK | 707 |

- Total instruments across all types: `761`.

## Query 4. Number of Holdings

Purpose: count the total number of portfolio holding rows currently stored.

```sql
USE IPMS;
GO

SELECT COUNT(*) AS total_holdings
FROM PORTFOLIO_HOLDINGS;
```

Observed result:

- Returned `28207`.

## Query 5. Number of Transactions

Purpose: count the total number of transaction rows recorded in the database.

```sql
USE IPMS;
GO

SELECT COUNT(*) AS total_transactions
FROM TRANSACTIONS;
```

Observed result:

- Returned `28211`.

## Query 6. Top 10 Accounts by Portfolio Count

Purpose: identify the accounts that currently own the most portfolios.

```sql
USE IPMS;
GO

SELECT TOP (10)
    u.email,
    COUNT(p.portfolio_id) AS portfolio_count
FROM USERS u
LEFT JOIN PORTFOLIOS p
    ON p.user_id = u.user_id
GROUP BY u.email
ORDER BY portfolio_count DESC, u.email;
```

Observed result:

- The highest current portfolio count is `10`.

| email | portfolio_count |
| --- | ---: |
| is5413.bulk0010@example.com | 10 |
| is5413.bulk0020@example.com | 10 |
| is5413.bulk0030@example.com | 10 |
| is5413.bulk0040@example.com | 10 |
| is5413.bulk0050@example.com | 10 |
| is5413.bulk0060@example.com | 10 |
| is5413.bulk0070@example.com | 10 |
| is5413.bulk0080@example.com | 10 |
| is5413.bulk0090@example.com | 10 |
| is5413.bulk0100@example.com | 10 |

## Query 7. Top 10 Portfolios by Live Market Value

Purpose: rank portfolios using current holdings quantity multiplied by each instrument's current price.

```sql
USE IPMS;
GO

SELECT TOP (10)
    p.portfolio_id,
    p.portfolio_name,
    u.email,
    p.currency,
    CAST(ROUND(SUM(ph.quantity * COALESCE(fi.current_price, 0)), 2) AS DECIMAL(18,2)) AS total_market_value
FROM PORTFOLIOS p
JOIN USERS u
    ON u.user_id = p.user_id
LEFT JOIN PORTFOLIO_HOLDINGS ph
    ON ph.portfolio_id = p.portfolio_id
LEFT JOIN FINANCIAL_INSTRUMENTS fi
    ON fi.instrument_id = ph.instrument_id
GROUP BY p.portfolio_id, p.portfolio_name, u.email, p.currency
ORDER BY total_market_value DESC, p.portfolio_id;
```

Observed result:

- The current top 10 portfolios are tied at `980000.00`.

| portfolio_id | portfolio_name | email | currency | total_market_value |
| ---: | --- | --- | --- | ---: |
| 86 | Balanced Global 10 | is5413.demo10@example.com | USD | 980000.00 |
| 96 | Core Compounders 20 | is5413.demo20@example.com | USD | 980000.00 |
| 106 | Healthcare Finance 30 | is5413.demo30@example.com | USD | 980000.00 |
| 116 | Income Stability 40 | is5413.demo40@example.com | USD | 980000.00 |
| 126 | Global Blue Chips 50 | is5413.demo50@example.com | USD | 980000.00 |
| 136 | Industrial Momentum 60 | is5413.demo60@example.com | USD | 980000.00 |
| 146 | Balanced Global 70 | is5413.demo70@example.com | USD | 980000.00 |
| 156 | Core Compounders 80 | is5413.demo80@example.com | USD | 980000.00 |
| 166 | Healthcare Finance 90 | is5413.demo90@example.com | USD | 980000.00 |
| 176 | Income Stability 100 | is5413.demo100@example.com | USD | 980000.00 |

## Query 8. Top 10 Most Widely Held Instruments

Purpose: show which instruments appear in the largest number of holding rows and their combined live market value across portfolios.

```sql
USE IPMS;
GO

SELECT TOP (10)
    fi.ticker_symbol,
    fi.name,
    fi.instrument_type,
    COUNT(*) AS holding_rows,
    CAST(ROUND(SUM(ph.quantity * COALESCE(fi.current_price, 0)), 2) AS DECIMAL(18,2)) AS aggregate_market_value
FROM PORTFOLIO_HOLDINGS ph
JOIN FINANCIAL_INSTRUMENTS fi
    ON fi.instrument_id = ph.instrument_id
GROUP BY fi.ticker_symbol, fi.name, fi.instrument_type
ORDER BY holding_rows DESC, aggregate_market_value DESC;
```

Observed result:

| ticker_symbol | name | instrument_type | holding_rows | aggregate_market_value |
| --- | --- | --- | ---: | ---: |
| 0941.HK | China Mobile Limited | STOCK | 1418 | 16670470.00 |
| 9999.HK | NetEase, Inc. | STOCK | 1184 | 14283830.00 |
| 1810.HK | Xiaomi Corporation | STOCK | 1178 | 14854560.00 |
| 0823.HK | Link Real Estate Investment Trust | STOCK | 1153 | 12885050.00 |
| 2800.HK | Tracker Fund Of Hong Kong | ETF | 1033 | 11733520.00 |
| NEE | NextEra Energy, Inc. | STOCK | 950 | 15227360.00 |
| LLY | Eli Lilly and Company | STOCK | 950 | 14436620.00 |
| VST | Vistra Corp. | STOCK | 941 | 14502500.00 |
| ELV | Elevance Health, Inc. | STOCK | 934 | 14566320.00 |
| IMO | Imperial Oil Limited | STOCK | 923 | 14755560.00 |

## Query 9. Latest Intraday Data Coverage

Purpose: check which instruments currently have intraday rows available for live analytics and when that coverage starts and ends.

```sql
USE IPMS;
GO

SELECT
    fi.ticker_symbol,
    COUNT(*) AS intraday_rows,
    MIN(ip.price_time_utc) AS first_time_utc,
    MAX(ip.price_time_utc) AS last_time_utc
FROM INTRADAY_PRICES ip
JOIN FINANCIAL_INSTRUMENTS fi
    ON fi.instrument_id = ip.instrument_id
GROUP BY fi.ticker_symbol
ORDER BY intraday_rows DESC, last_time_utc DESC;
```

Observed result:

| ticker_symbol | intraday_rows | first_time_utc | last_time_utc |
| --- | ---: | --- | --- |
| BTC-USD | 356 | 2026-04-04 00:00:00 | 2026-04-04 06:39:00 |

- At the time of capture, `BTC-USD` is the only ticker with intraday rows in `INTRADAY_PRICES`.

## Query 10. True Minute-by-Minute Live Return History

Purpose: show the true minute-by-minute live return history for the latest available intraday session, using the previous historical close as the return baseline.

```sql
USE IPMS;
GO

DECLARE @TickerSymbol NVARCHAR(20) = 'BTC-USD';

WITH TargetInstrument AS
(
    SELECT
        instrument_id,
        ticker_symbol,
        name
    FROM FINANCIAL_INSTRUMENTS
    WHERE ticker_symbol = @TickerSymbol
),
LatestSession AS
(
    SELECT
        CAST(MAX(ip.price_time_utc) AS date) AS latest_trade_date
    FROM INTRADAY_PRICES ip
    JOIN TargetInstrument ti
        ON ti.instrument_id = ip.instrument_id
),
MinuteHistory AS
(
    SELECT
        ti.instrument_id,
        ti.ticker_symbol,
        ti.name,
        ip.price_time_utc,
        ip.open_price,
        ip.high_price,
        ip.low_price,
        ip.close_price,
        ip.volume,
        prev.previous_close,
        CASE
            WHEN prev.previous_close IS NULL THEN NULL
            ELSE ip.close_price - prev.previous_close
        END AS price_change,
        CASE
            WHEN prev.previous_close IS NULL OR prev.previous_close = 0 THEN NULL
            ELSE ROUND(((ip.close_price - prev.previous_close) * 100.0) / prev.previous_close, 4)
        END AS percentage_change
    FROM INTRADAY_PRICES ip
    JOIN TargetInstrument ti
        ON ti.instrument_id = ip.instrument_id
    CROSS JOIN LatestSession ls
    OUTER APPLY
    (
        SELECT TOP (1)
            hp.close_price AS previous_close
        FROM HISTORICAL_PRICES hp
        WHERE hp.instrument_id = ip.instrument_id
          AND hp.price_date < CAST(ip.price_time_utc AS date)
        ORDER BY hp.price_date DESC
    ) prev
    WHERE CAST(ip.price_time_utc AS date) = ls.latest_trade_date
)
SELECT
    instrument_id,
    ticker_symbol,
    name,
    price_time_utc,
    open_price,
    high_price,
    low_price,
    close_price,
    volume,
    previous_close,
    price_change,
    percentage_change
FROM MinuteHistory
ORDER BY price_time_utc;
```

Observed result:

- Returned `356` rows for `BTC-USD`.
- Session window: `2026-04-04 00:00:00` to `2026-04-04 06:39:00` UTC.
- Close-price range during the captured session: `66790.7891` to `66955.8594`.
- Previous-close reference used for return calculation: `66930.6797`.

First 5 rows:

| price_time_utc | close_price | previous_close | price_change | percentage_change |
| --- | ---: | ---: | ---: | ---: |
| 2026-04-04 00:00:00 | 66937.2500 | 66930.6797 | 6.5703 | 0.0098 |
| 2026-04-04 00:01:00 | 66931.4766 | 66930.6797 | 0.7969 | 0.0012 |
| 2026-04-04 00:02:00 | 66927.4922 | 66930.6797 | -3.1875 | -0.0048 |
| 2026-04-04 00:03:00 | 66909.8438 | 66930.6797 | -20.8359 | -0.0311 |
| 2026-04-04 00:04:00 | 66899.9219 | 66930.6797 | -30.7578 | -0.0460 |

Latest 5 captured rows:

| price_time_utc | close_price | previous_close | price_change | percentage_change |
| --- | ---: | ---: | ---: | ---: |
| 2026-04-04 06:34:00 | 66920.1016 | 66930.6797 | -10.5781 | -0.0158 |
| 2026-04-04 06:35:00 | 66930.7031 | 66930.6797 | 0.0234 | 0.0000 |
| 2026-04-04 06:36:00 | 66928.2891 | 66930.6797 | -2.3906 | -0.0036 |
| 2026-04-04 06:37:00 | 66922.3906 | 66930.6797 | -8.2891 | -0.0124 |
| 2026-04-04 06:39:00 | 66922.8400 | 66930.6797 | -7.8397 | -0.0117 |

- The stored data is minute-granular, but the current captured session is not perfectly gap-free because `06:38:00` is missing.
