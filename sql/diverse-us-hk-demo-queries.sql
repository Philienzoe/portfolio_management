USE IPMS;

-- 1. Show the 100 IS5413 demo users
SELECT
    user_id,
    email,
    first_name,
    last_name,
    created_at
FROM USERS
ORDER BY email;

-- 2. Show their portfolios
SELECT
    u.email,
    p.portfolio_id,
    p.portfolio_name,
    p.description,
    p.created_at
FROM PORTFOLIOS p
JOIN USERS u ON u.user_id = p.user_id
ORDER BY u.email;

-- 3. Show all holdings with market value
SELECT
    u.email,
    p.portfolio_name,
    fi.ticker_symbol,
    fi.name,
    ph.quantity,
    ph.average_cost,
    fi.current_price,
    ph.quantity * fi.current_price AS market_value
FROM PORTFOLIO_HOLDINGS ph
JOIN PORTFOLIOS p ON p.portfolio_id = ph.portfolio_id
JOIN USERS u ON u.user_id = p.user_id
JOIN FINANCIAL_INSTRUMENTS fi ON fi.instrument_id = ph.instrument_id
ORDER BY u.email, market_value DESC;

-- 4. Portfolio totals
SELECT
    u.email,
    p.portfolio_name,
    COUNT(ph.instrument_id) AS holding_count,
    SUM(ph.quantity * fi.current_price) AS total_market_value
FROM PORTFOLIOS p
JOIN USERS u ON u.user_id = p.user_id
LEFT JOIN PORTFOLIO_HOLDINGS ph ON ph.portfolio_id = p.portfolio_id
LEFT JOIN FINANCIAL_INSTRUMENTS fi ON fi.instrument_id = ph.instrument_id
GROUP BY u.email, p.portfolio_name
ORDER BY u.email;

-- 5. Show the US/HK mix and ticker diversity
SELECT
    COUNT(DISTINCT fi.ticker_symbol) AS unique_tickers_used,
    SUM(CASE WHEN fi.ticker_symbol LIKE '%.HK' THEN 1 ELSE 0 END) AS hk_positions,
    SUM(CASE WHEN fi.ticker_symbol NOT LIKE '%.HK' THEN 1 ELSE 0 END) AS us_positions
FROM PORTFOLIO_HOLDINGS ph
JOIN PORTFOLIOS p ON p.portfolio_id = ph.portfolio_id
JOIN USERS u ON u.user_id = p.user_id
JOIN FINANCIAL_INSTRUMENTS fi ON fi.instrument_id = ph.instrument_id
