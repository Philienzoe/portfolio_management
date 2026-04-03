param(
    [string]$DbServer = "localhost\SQLEXPRESS",
    [string]$Database = "IPMS",
    [int]$AccountCount = 1000,
    [string]$EmailPrefix = "is5413.bulk",
    [string]$EmailDomain = "example.com",
    [switch]$ResetExistingPrefix
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($AccountCount -le 0) {
    throw "AccountCount must be greater than zero."
}

$normalizedPrefix = $EmailPrefix.Trim().ToLowerInvariant()
$normalizedDomain = $EmailDomain.Trim().ToLowerInvariant()

$sql = @"
USE [$Database];
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;

DECLARE @AccountCount INT = $AccountCount;
DECLARE @EmailPrefix NVARCHAR(100) = N'$normalizedPrefix';
DECLARE @EmailDomain NVARCHAR(100) = N'$normalizedDomain';
DECLARE @PasswordHash NVARCHAR(255);
DECLARE @ExistingBase INT = 0;
DECLARE @ResetExistingPrefix BIT = $(if ($ResetExistingPrefix.IsPresent) { 1 } else { 0 });

SELECT TOP (1)
    @PasswordHash = password_hash
FROM USERS
WHERE LOWER(email) LIKE 'is5413.demo%@example.com'
   OR LOWER(email) = 'stockcircle.demo@example.com'
ORDER BY CASE WHEN LOWER(email) LIKE 'is5413.demo01%@example.com' THEN 0 ELSE 1 END, user_id;

IF @PasswordHash IS NULL
BEGIN
    THROW 50000, 'Unable to find a reusable demo password hash.', 1;
END;

CREATE TABLE #NewUsers
(
    account_no INT NOT NULL PRIMARY KEY,
    user_id INT NOT NULL,
    email NVARCHAR(255) NOT NULL
);

CREATE TABLE #UserSource
(
    account_no INT NOT NULL PRIMARY KEY,
    email NVARCHAR(255) NOT NULL,
    suffix NVARCHAR(10) NOT NULL
);

CREATE TABLE #NewPortfolios
(
    account_no INT NOT NULL,
    portfolio_no INT NOT NULL,
    portfolio_id INT NOT NULL,
    user_id INT NOT NULL,
    PRIMARY KEY (portfolio_id)
);

CREATE TABLE #PortfolioSource
(
    account_no INT NOT NULL,
    user_id INT NOT NULL,
    portfolio_no INT NOT NULL,
    portfolio_name NVARCHAR(100) NOT NULL,
    description NVARCHAR(500) NOT NULL,
    currency NVARCHAR(3) NOT NULL,
    created_at DATETIME2 NOT NULL,
    PRIMARY KEY (user_id, portfolio_name)
);

CREATE TABLE #UsPool
(
    row_num INT NOT NULL PRIMARY KEY,
    instrument_id INT NOT NULL,
    ticker_symbol NVARCHAR(20) NOT NULL,
    current_price DECIMAL(19,4) NOT NULL
);

CREATE TABLE #HkPool
(
    row_num INT NOT NULL PRIMARY KEY,
    instrument_id INT NOT NULL,
    ticker_symbol NVARCHAR(20) NOT NULL,
    current_price DECIMAL(19,4) NOT NULL
);

CREATE TABLE #SeedPositions
(
    portfolio_id INT NOT NULL,
    account_no INT NOT NULL,
    portfolio_no INT NOT NULL,
    instrument_id INT NOT NULL,
    holding_rank INT NOT NULL,
    region_code NVARCHAR(2) NOT NULL,
    quantity DECIMAL(18,8) NOT NULL,
    price_per_unit DECIMAL(19,4) NOT NULL,
    transaction_date DATETIME2 NOT NULL,
    PRIMARY KEY (portfolio_id, instrument_id)
);

BEGIN TRY
    BEGIN TRANSACTION;

    IF @ResetExistingPrefix = 1
    BEGIN
        DELETE FROM USERS
        WHERE LOWER(email) LIKE @EmailPrefix + '%@' + @EmailDomain;
    END;

    IF @ResetExistingPrefix = 0
    BEGIN
        SELECT
            @ExistingBase = ISNULL(MAX(TRY_CONVERT(INT, SUBSTRING(email, LEN(@EmailPrefix) + 1, CHARINDEX('@', email) - LEN(@EmailPrefix) - 1))), 0)
        FROM USERS
        WHERE LOWER(email) LIKE @EmailPrefix + '%@' + @EmailDomain;
    END;

    ;WITH Numbers AS
    (
        SELECT TOP (@AccountCount)
            ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
        FROM sys.all_objects a
        CROSS JOIN sys.all_objects b
    )
    INSERT INTO #UserSource
    (
        account_no,
        email,
        suffix
    )
    SELECT
        @ExistingBase + n AS account_no,
        LOWER(CONCAT(
            @EmailPrefix,
            RIGHT(CONCAT(REPLICATE('0', 4), CAST(@ExistingBase + n AS VARCHAR(10))), 4),
            '@',
            @EmailDomain)) AS email,
        RIGHT(CONCAT(REPLICATE('0', 4), CAST(@ExistingBase + n AS VARCHAR(10))), 4) AS suffix
    FROM Numbers;

    INSERT INTO USERS
    (
        email,
        password_hash,
        first_name,
        last_name,
        created_at
    )
    SELECT
        source.email,
        @PasswordHash,
        N'IS5413',
        CONCAT(N'Bulk ', source.suffix),
        DATEADD(SECOND, -source.account_no, SYSUTCDATETIME())
    FROM #UserSource source
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM USERS existing_user
        WHERE existing_user.email = source.email
    );

    INSERT INTO #NewUsers
    (
        account_no,
        user_id,
        email
    )
    SELECT
        source.account_no,
        u.user_id,
        u.email
    FROM #UserSource source
    JOIN USERS u
        ON u.email = source.email;

    ;WITH PortfolioNumbers AS
    (
        SELECT portfolio_no
        FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) AS v(portfolio_no)
    ),
    PortfolioSource AS
    (
        SELECT
            nu.account_no,
            nu.user_id,
            pn.portfolio_no
        FROM #NewUsers nu
        JOIN PortfolioNumbers pn
            ON pn.portfolio_no <= ((nu.account_no - 1) % 10) + 1
    )
    INSERT INTO #PortfolioSource
    (
        account_no,
        user_id,
        portfolio_no,
        portfolio_name,
        description,
        currency,
        created_at
    )
    SELECT
        source.account_no,
        source.user_id,
        source.portfolio_no,
        CONCAT(N'Portfolio ', RIGHT(CONCAT('00', CAST(source.portfolio_no AS VARCHAR(2))), 2)),
        CONCAT(
            N'Bulk demo portfolio ',
            source.portfolio_no,
            N' for account ',
            RIGHT(CONCAT(REPLICATE('0', 4), CAST(source.account_no AS VARCHAR(10))), 4)),
        CASE WHEN source.portfolio_no % 2 = 0 THEN N'USD' ELSE N'HKD' END,
        DATEADD(DAY, -((source.account_no % 365) + source.portfolio_no), SYSUTCDATETIME())
    FROM PortfolioSource source;

    INSERT INTO PORTFOLIOS
    (
        user_id,
        portfolio_name,
        description,
        currency,
        created_at
    )
    SELECT
        source.user_id,
        source.portfolio_name,
        source.description,
        source.currency,
        source.created_at
    FROM #PortfolioSource source;

    INSERT INTO #NewPortfolios
    (
        account_no,
        portfolio_no,
        portfolio_id,
        user_id
    )
    SELECT
        source.account_no,
        source.portfolio_no,
        p.portfolio_id,
        source.user_id
    FROM #PortfolioSource source
    JOIN PORTFOLIOS p
        ON p.user_id = source.user_id
       AND p.portfolio_name = source.portfolio_name;

    INSERT INTO #UsPool (row_num, instrument_id, ticker_symbol, current_price)
    SELECT
        ROW_NUMBER() OVER (ORDER BY ticker_symbol),
        instrument_id,
        ticker_symbol,
        current_price
    FROM FINANCIAL_INSTRUMENTS
    WHERE instrument_type IN ('STOCK', 'ETF')
      AND ticker_symbol NOT LIKE '%.HK'
      AND ticker_symbol NOT LIKE '%-USD'
      AND current_price IS NOT NULL
      AND current_price > 0;

    INSERT INTO #HkPool (row_num, instrument_id, ticker_symbol, current_price)
    SELECT
        ROW_NUMBER() OVER (ORDER BY ticker_symbol),
        instrument_id,
        ticker_symbol,
        current_price
    FROM FINANCIAL_INSTRUMENTS
    WHERE instrument_type IN ('STOCK', 'ETF')
      AND ticker_symbol LIKE '%.HK'
      AND current_price IS NOT NULL
      AND current_price > 0;

    IF (SELECT COUNT(*) FROM #UsPool) < 10
    BEGIN
        THROW 50001, 'Not enough non-HK instruments are available for bulk seeding.', 1;
    END;

    IF (SELECT COUNT(*) FROM #HkPool) < 5
    BEGIN
        THROW 50002, 'Not enough HK instruments are available for bulk seeding.', 1;
    END;

    ;WITH UsRanked AS
    (
        SELECT
            np.portfolio_id,
            np.account_no,
            np.portfolio_no,
            up.instrument_id,
            up.current_price,
            ROW_NUMBER() OVER
            (
                PARTITION BY np.portfolio_id
                ORDER BY CHECKSUM(CONCAT(np.account_no, ':', np.portfolio_no, ':US:', up.ticker_symbol)), up.row_num
            ) AS rn
        FROM #NewPortfolios np
        CROSS JOIN #UsPool up
    ),
    HkRanked AS
    (
        SELECT
            np.portfolio_id,
            np.account_no,
            np.portfolio_no,
            hp.instrument_id,
            hp.current_price,
            ROW_NUMBER() OVER
            (
                PARTITION BY np.portfolio_id
                ORDER BY CHECKSUM(CONCAT(np.account_no, ':', np.portfolio_no, ':HK:', hp.ticker_symbol)), hp.row_num
            ) AS rn
        FROM #NewPortfolios np
        CROSS JOIN #HkPool hp
    ),
    RawPositions AS
    (
        SELECT
            portfolio_id,
            account_no,
            portfolio_no,
            instrument_id,
            current_price,
            rn AS holding_rank,
            N'US' AS region_code
        FROM UsRanked
        WHERE rn <= 3

        UNION ALL

        SELECT
            portfolio_id,
            account_no,
            portfolio_no,
            instrument_id,
            current_price,
            rn + 3 AS holding_rank,
            N'HK' AS region_code
        FROM HkRanked
        WHERE rn <= 2
    )
    INSERT INTO #SeedPositions
    (
        portfolio_id,
        account_no,
        portfolio_no,
        instrument_id,
        holding_rank,
        region_code,
        quantity,
        price_per_unit,
        transaction_date
    )
    SELECT
        rp.portfolio_id,
        rp.account_no,
        rp.portfolio_no,
        rp.instrument_id,
        rp.holding_rank,
        rp.region_code,
        CAST(ROUND(
            (
                (
                    CAST(20000 AS DECIMAL(19,4))
                    + CAST((rp.account_no % 25) * 2500 AS DECIMAL(19,4))
                    + CAST(rp.portfolio_no * 4000 AS DECIMAL(19,4))
                )
                * CASE rp.holding_rank
                    WHEN 1 THEN CAST(0.26 AS DECIMAL(9,4))
                    WHEN 2 THEN CAST(0.22 AS DECIMAL(9,4))
                    WHEN 3 THEN CAST(0.18 AS DECIMAL(9,4))
                    WHEN 4 THEN CAST(0.18 AS DECIMAL(9,4))
                    ELSE CAST(0.16 AS DECIMAL(9,4))
                  END
            ) / rp.current_price, 8) AS DECIMAL(18,8)) AS quantity,
        CAST(ROUND(
            rp.current_price *
            CASE rp.holding_rank
                WHEN 1 THEN CAST(0.94 AS DECIMAL(9,4))
                WHEN 2 THEN CAST(0.92 AS DECIMAL(9,4))
                WHEN 3 THEN CAST(0.91 AS DECIMAL(9,4))
                WHEN 4 THEN CAST(0.93 AS DECIMAL(9,4))
                ELSE CAST(0.90 AS DECIMAL(9,4))
            END, 4) AS DECIMAL(19,4)) AS price_per_unit,
        DATEADD(DAY, -((rp.account_no % 90) + (rp.portfolio_no * 2) + (rp.holding_rank * 5)), SYSUTCDATETIME()) AS transaction_date
    FROM RawPositions rp;

    INSERT INTO TRANSACTIONS
    (
        portfolio_id,
        instrument_id,
        transaction_type,
        quantity,
        price_per_unit,
        transaction_date,
        fees,
        notes
    )
    SELECT
        portfolio_id,
        instrument_id,
        'BUY',
        quantity,
        price_per_unit,
        transaction_date,
        CAST(0 AS DECIMAL(19,4)),
        N'Bulk seeded demo position'
    FROM #SeedPositions;

    INSERT INTO PORTFOLIO_HOLDINGS
    (
        portfolio_id,
        instrument_id,
        quantity,
        average_cost,
        last_updated
    )
    SELECT
        portfolio_id,
        instrument_id,
        quantity,
        price_per_unit,
        transaction_date
    FROM #SeedPositions;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;

SELECT
    COUNT(*) AS inserted_users
FROM #NewUsers;

SELECT
    COUNT(*) AS inserted_portfolios
FROM #NewPortfolios;

SELECT
    COUNT(*) AS inserted_positions
FROM #SeedPositions;
"@

$tempSql = Join-Path $env:TEMP ("ipms-bulk-seed-{0}.sql" -f ([Guid]::NewGuid().ToString("N")))
Set-Content -Path $tempSql -Value $sql -Encoding ASCII

try {
    & sqlcmd -S $DbServer -E -C -i $tempSql
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd exited with code $LASTEXITCODE."
    }
}
finally {
    Remove-Item -Path $tempSql -ErrorAction SilentlyContinue
}
