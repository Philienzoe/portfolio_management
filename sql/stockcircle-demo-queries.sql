USE IPMS;

-- 1. Show the imported Stockcircle demo portfolios
SELECT
    p.portfolio_id,
    p.portfolio_name,
    p.description,
    p.created_at
FROM PORTFOLIOS p
JOIN USERS u ON u.user_id = p.user_id
WHERE u.email = 'stockcircle.demo@example.com'
ORDER BY p.portfolio_name;

-- 2. Show each portfolio's total market value
SELECT
    p.portfolio_name,
    COUNT(ph.instrument_id) AS holding_count,
    SUM(ph.quantity * fi.current_price) AS total_market_value
FROM PORTFOLIOS p
JOIN USERS u ON u.user_id = p.user_id
LEFT JOIN PORTFOLIO_HOLDINGS ph ON ph.portfolio_id = p.portfolio_id
LEFT JOIN FINANCIAL_INSTRUMENTS fi ON fi.instrument_id = ph.instrument_id
WHERE u.email = 'stockcircle.demo@example.com'
GROUP BY p.portfolio_name
ORDER BY total_market_value DESC;

-- 3. Show all imported holdings
SELECT
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
WHERE u.email = 'stockcircle.demo@example.com'
ORDER BY p.portfolio_name, market_value DESC;

-- 4. Show the transaction history used to build the demo portfolios
SELECT
    p.portfolio_name,
    fi.ticker_symbol,
    t.transaction_type,
    t.quantity,
    t.price_per_unit,
    t.total_amount,
    t.transaction_date,
    t.notes
FROM TRANSACTIONS t
JOIN PORTFOLIOS p ON p.portfolio_id = t.portfolio_id
JOIN USERS u ON u.user_id = p.user_id
JOIN FINANCIAL_INSTRUMENTS fi ON fi.instrument_id = t.instrument_id
WHERE u.email = 'stockcircle.demo@example.com'
ORDER BY p.portfolio_name, t.transaction_date DESC;
