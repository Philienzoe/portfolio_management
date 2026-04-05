# IPMS Mermaid ERD

```mermaid
%%{init: {"er": {"layoutDirection": "TB"}} }%%
erDiagram
    direction TB

    USERS {
        int user_id PK
        nvarchar email UK
        nvarchar password_hash
        nvarchar first_name
        nvarchar last_name
        datetime2 created_at
    }

    PORTFOLIOS {
        int portfolio_id PK
        int user_id FK
        nvarchar portfolio_name
        nvarchar description
        nvarchar currency
        datetime2 created_at
    }

    FINANCIAL_INSTRUMENTS {
        int instrument_id PK
        nvarchar ticker_symbol UK
        nvarchar instrument_type
        nvarchar name
        decimal current_price
        datetime2 last_updated
    }

    HISTORICAL_PRICES {
        int price_id PK
        int instrument_id FK
        date price_date
        decimal open_price
        decimal high_price
        decimal low_price
        decimal close_price
        decimal adjusted_close
        bigint volume
    }

    TRANSACTIONS {
        int transaction_id PK
        int portfolio_id FK
        int instrument_id FK
        nvarchar transaction_type
        decimal quantity
        decimal price_per_unit
        datetime2 transaction_date
        decimal fees
        nvarchar notes
    }

    PORTFOLIO_HOLDINGS {
        int portfolio_id PK, FK
        int instrument_id PK, FK
        decimal quantity
        decimal average_cost
        datetime2 last_updated
    }

    INTRADAY_PRICES {
        int intraday_price_id PK
        int instrument_id FK
        datetime2 price_time_utc
        decimal open_price
        decimal high_price
        decimal low_price
        decimal close_price
        bigint volume
    }

    STOCKS {
        int instrument_id PK, FK
        decimal market_cap
        decimal pe_ratio
        decimal dividend_yield
        int exchange_id FK
        nvarchar quote_currency
        int industry_id FK
    }

    ETFS {
        int instrument_id PK, FK
        nvarchar asset_class
        decimal expense_ratio
        nvarchar issuer
        nvarchar tracking_index
        int exchange_id FK
        nvarchar quote_currency
    }

    CRYPTOCURRENCIES {
        int instrument_id PK, FK
        nvarchar blockchain
        nvarchar hashing_algorithm
        decimal max_supply
        decimal circulating_supply
        nvarchar base_asset_symbol
        nvarchar quote_currency
    }

    STOCK_EXCHANGES {
        int exchange_id PK
        nvarchar mic_code
        nvarchar name
        nvarchar country
        nvarchar city
        nvarchar timezone
    }

    INDUSTRIES {
        int industry_id PK
        nvarchar industry_name UK
        int sector_id FK
    }

    SECTORS {
        int sector_id PK
        nvarchar sector_name
    }

    USERS ||--o{ PORTFOLIOS : "1:N"

    PORTFOLIOS ||--o{ TRANSACTIONS : "1:N"
    PORTFOLIOS ||--o{ PORTFOLIO_HOLDINGS : "1:N"

    FINANCIAL_INSTRUMENTS ||--o{ TRANSACTIONS : "1:N"
    FINANCIAL_INSTRUMENTS ||--o{ PORTFOLIO_HOLDINGS : "1:N"
    FINANCIAL_INSTRUMENTS ||--o{ HISTORICAL_PRICES : "1:N"
    FINANCIAL_INSTRUMENTS ||--o{ INTRADAY_PRICES : "1:N"
    FINANCIAL_INSTRUMENTS ||--o| STOCKS : "1:1"
    FINANCIAL_INSTRUMENTS ||--o| ETFS : "1:1"
    FINANCIAL_INSTRUMENTS ||--o| CRYPTOCURRENCIES : "1:1"

    STOCK_EXCHANGES o|--o{ STOCKS : "1:N"
    STOCK_EXCHANGES o|--o{ ETFS : "1:N"
    INDUSTRIES o|--o{ STOCKS : "1:N"
    SECTORS ||--o{ INDUSTRIES : "1:N"
```

Notes:

- This Mermaid ERD matches the current live `IPMS` database schema.
- `REALTIME_PRICE_SNAPSHOTS` is not included because that table does not currently exist in the live database.
- Mermaid already shows cardinality with crow's foot symbols such as `||--o{` and `||--o|`; the added labels make the relationships read more like `1:N` and `1:1`.
