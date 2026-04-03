USE [IPMS];
GO

DECLARE @userId INT = 1;

SELECT p.portfolio_id,
       p.portfolio_name,
       p.currency,
       SUM(ph.quantity * fi.current_price) AS total_value
FROM PORTFOLIOS AS p
LEFT JOIN PORTFOLIO_HOLDINGS AS ph
    ON p.portfolio_id = ph.portfolio_id
LEFT JOIN FINANCIAL_INSTRUMENTS AS fi
    ON ph.instrument_id = fi.instrument_id
WHERE p.user_id = @userId
GROUP BY p.portfolio_id, p.portfolio_name, p.currency
ORDER BY total_value DESC;
GO

DECLARE @portfolioId INT = 1;

WITH PortfolioValues AS
(
    SELECT fi.instrument_type,
           SUM(ph.quantity * fi.current_price) AS type_value
    FROM PORTFOLIO_HOLDINGS AS ph
    INNER JOIN FINANCIAL_INSTRUMENTS AS fi
        ON ph.instrument_id = fi.instrument_id
    WHERE ph.portfolio_id = @portfolioId
    GROUP BY fi.instrument_type
)
SELECT instrument_type,
       type_value,
       ROUND(type_value * 100.0 / SUM(type_value) OVER (), 2) AS allocation_pct
FROM PortfolioValues
ORDER BY type_value DESC;
GO

SELECT fi.ticker_symbol,
       fi.name,
       ph.quantity,
       ph.average_cost,
       fi.current_price,
       (fi.current_price - ph.average_cost) * ph.quantity AS unrealized_pnl,
       ROUND(((fi.current_price - ph.average_cost) / NULLIF(ph.average_cost, 0)) * 100.0, 2) AS pnl_pct
FROM PORTFOLIO_HOLDINGS AS ph
INNER JOIN FINANCIAL_INSTRUMENTS AS fi
    ON ph.instrument_id = fi.instrument_id
WHERE ph.portfolio_id = @portfolioId
ORDER BY unrealized_pnl DESC;
GO

SELECT s.sector,
       COUNT(*) AS num_holdings,
       SUM(ph.quantity * fi.current_price) AS sector_value
FROM PORTFOLIO_HOLDINGS AS ph
INNER JOIN STOCKS AS s
    ON ph.instrument_id = s.instrument_id
INNER JOIN FINANCIAL_INSTRUMENTS AS fi
    ON ph.instrument_id = fi.instrument_id
WHERE ph.portfolio_id = @portfolioId
GROUP BY s.sector
ORDER BY sector_value DESC;
GO

DECLARE @instrumentId INT = 1;

SELECT YEAR(price_date) AS [year],
       MONTH(price_date) AS [month],
       AVG(close_price) AS average_close,
       MAX(high_price) AS monthly_high,
       MIN(low_price) AS monthly_low
FROM HISTORICAL_PRICES
WHERE instrument_id = @instrumentId
GROUP BY YEAR(price_date), MONTH(price_date)
ORDER BY [year], [month];
GO
