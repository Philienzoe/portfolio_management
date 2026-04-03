using Ipms.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ipms.Api.Data;

public sealed class IpmsDbContext(DbContextOptions<IpmsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<StockExchange> StockExchanges => Set<StockExchange>();
    public DbSet<FinancialInstrument> FinancialInstruments => Set<FinancialInstrument>();
    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<Etf> Etfs => Set<Etf>();
    public DbSet<Cryptocurrency> Cryptocurrencies => Set<Cryptocurrency>();
    public DbSet<TransactionRecord> Transactions => Set<TransactionRecord>();
    public DbSet<PortfolioHolding> PortfolioHoldings => Set<PortfolioHolding>();
    public DbSet<HistoricalPrice> HistoricalPrices => Set<HistoricalPrice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var transactionKindConverter = new ValueConverter<TransactionKind, string>(
            value => value == TransactionKind.Buy
                ? "BUY"
                : value == TransactionKind.Sell
                    ? "SELL"
                    : "DIVIDEND",
            value => value == "BUY"
                ? TransactionKind.Buy
                : value == "SELL"
                    ? TransactionKind.Sell
                    : TransactionKind.Dividend);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("USERS");
            entity.HasKey(e => e.UserId);

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
            entity.Property(e => e.FirstName).HasColumnName("first_name").HasMaxLength(100);
            entity.Property(e => e.LastName).HasColumnName("last_name").HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("SYSDATETIME()");

            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<Portfolio>(entity =>
        {
            entity.ToTable("PORTFOLIOS");
            entity.HasKey(e => e.PortfolioId);

            entity.Property(e => e.PortfolioId).HasColumnName("portfolio_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.PortfolioName).HasColumnName("portfolio_name").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
            entity.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("USD").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("SYSDATETIME()");

            entity.HasIndex(e => new { e.UserId, e.PortfolioName }).IsUnique();
            entity.HasOne(e => e.User)
                .WithMany(e => e.Portfolios)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockExchange>(entity =>
        {
            entity.ToTable("STOCK_EXCHANGES");
            entity.HasKey(e => e.ExchangeId);

            entity.Property(e => e.ExchangeId).HasColumnName("exchange_id");
            entity.Property(e => e.MicCode).HasColumnName("mic_code").HasMaxLength(10).IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            entity.Property(e => e.Country).HasColumnName("country").HasMaxLength(80).IsRequired();
            entity.Property(e => e.City).HasColumnName("city").HasMaxLength(80).IsRequired();
            entity.Property(e => e.Timezone).HasColumnName("timezone").HasMaxLength(40).IsRequired();

            entity.HasIndex(e => e.MicCode).IsUnique();
        });

        modelBuilder.Entity<FinancialInstrument>(entity =>
        {
            entity.ToTable("FINANCIAL_INSTRUMENTS", table =>
            {
                table.HasCheckConstraint(
                    "CK_FINANCIAL_INSTRUMENTS_TYPE",
                    "[instrument_type] IN ('STOCK','ETF','CRYPTO')");
            });

            entity.HasKey(e => e.InstrumentId);

            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id");
            entity.Property(e => e.TickerSymbol).HasColumnName("ticker_symbol").HasMaxLength(20).IsRequired();
            entity.Property(e => e.InstrumentType).HasColumnName("instrument_type").HasMaxLength(20).IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.CurrentPrice).HasColumnName("current_price").HasPrecision(19, 4);
            entity.Property(e => e.LastUpdated).HasColumnName("last_updated").HasDefaultValueSql("SYSDATETIME()");

            entity.HasIndex(e => e.TickerSymbol).IsUnique();
        });

        modelBuilder.Entity<Stock>(entity =>
        {
            entity.ToTable("STOCKS");
            entity.HasKey(e => e.InstrumentId);

            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id").ValueGeneratedNever();
            entity.Property(e => e.Sector).HasColumnName("sector").HasMaxLength(100);
            entity.Property(e => e.Industry).HasColumnName("industry").HasMaxLength(100);
            entity.Property(e => e.MarketCap).HasColumnName("market_cap").HasPrecision(19, 2);
            entity.Property(e => e.PeRatio).HasColumnName("pe_ratio").HasPrecision(8, 4);
            entity.Property(e => e.DividendYield).HasColumnName("dividend_yield").HasPrecision(8, 4);
            entity.Property(e => e.ExchangeId).HasColumnName("exchange_id");

            entity.HasOne(e => e.Instrument)
                .WithOne(e => e.Stock)
                .HasForeignKey<Stock>(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Exchange)
                .WithMany(e => e.Stocks)
                .HasForeignKey(e => e.ExchangeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Etf>(entity =>
        {
            entity.ToTable("ETFS");
            entity.HasKey(e => e.InstrumentId);

            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id").ValueGeneratedNever();
            entity.Property(e => e.AssetClass).HasColumnName("asset_class").HasMaxLength(100);
            entity.Property(e => e.ExpenseRatio).HasColumnName("expense_ratio").HasPrecision(8, 4);
            entity.Property(e => e.Issuer).HasColumnName("issuer").HasMaxLength(120);
            entity.Property(e => e.TrackingIndex).HasColumnName("tracking_index").HasMaxLength(120);
            entity.Property(e => e.ExchangeId).HasColumnName("exchange_id");

            entity.HasOne(e => e.Instrument)
                .WithOne(e => e.Etf)
                .HasForeignKey<Etf>(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Exchange)
                .WithMany(e => e.Etfs)
                .HasForeignKey(e => e.ExchangeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Cryptocurrency>(entity =>
        {
            entity.ToTable("CRYPTOCURRENCIES");
            entity.HasKey(e => e.InstrumentId);

            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id").ValueGeneratedNever();
            entity.Property(e => e.Blockchain).HasColumnName("blockchain").HasMaxLength(100);
            entity.Property(e => e.HashingAlgorithm).HasColumnName("hashing_algorithm").HasMaxLength(100);
            entity.Property(e => e.MaxSupply).HasColumnName("max_supply").HasPrecision(28, 8);
            entity.Property(e => e.CirculatingSupply).HasColumnName("circulating_supply").HasPrecision(28, 8);

            entity.HasOne(e => e.Instrument)
                .WithOne(e => e.Cryptocurrency)
                .HasForeignKey<Cryptocurrency>(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TransactionRecord>(entity =>
        {
            entity.ToTable("TRANSACTIONS", table =>
            {
                table.HasCheckConstraint(
                    "CK_TRANSACTIONS_TYPE",
                    "[transaction_type] IN ('BUY','SELL','DIVIDEND')");
                table.HasCheckConstraint("CK_TRANSACTIONS_QUANTITY", "[quantity] > 0");
                table.HasCheckConstraint("CK_TRANSACTIONS_FEES", "[fees] >= 0");
            });

            entity.HasKey(e => e.TransactionId);

            entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
            entity.Property(e => e.PortfolioId).HasColumnName("portfolio_id");
            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id");
            entity.Property(e => e.TransactionType)
                .HasColumnName("transaction_type")
                .HasConversion(transactionKindConverter)
                .HasMaxLength(10)
                .IsRequired();
            entity.Property(e => e.Quantity).HasColumnName("quantity").HasPrecision(18, 8).IsRequired();
            entity.Property(e => e.PricePerUnit).HasColumnName("price_per_unit").HasPrecision(19, 4).IsRequired();
            entity.Property(e => e.TotalAmount)
                .HasColumnName("total_amount")
                .HasPrecision(28, 8)
                .HasComputedColumnSql("[quantity] * [price_per_unit]", stored: true);
            entity.Property(e => e.TransactionDate).HasColumnName("transaction_date").IsRequired();
            entity.Property(e => e.Fees).HasColumnName("fees").HasPrecision(19, 4).HasDefaultValue(0m);
            entity.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(500);

            entity.HasIndex(e => new { e.PortfolioId, e.TransactionDate });

            entity.HasOne(e => e.Portfolio)
                .WithMany(e => e.Transactions)
                .HasForeignKey(e => e.PortfolioId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Instrument)
                .WithMany(e => e.Transactions)
                .HasForeignKey(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PortfolioHolding>(entity =>
        {
            entity.ToTable("PORTFOLIO_HOLDINGS", table =>
            {
                table.HasCheckConstraint("CK_PORTFOLIO_HOLDINGS_QUANTITY", "[quantity] >= 0");
                table.HasCheckConstraint("CK_PORTFOLIO_HOLDINGS_AVERAGE_COST", "[average_cost] >= 0");
            });

            entity.HasKey(e => new { e.PortfolioId, e.InstrumentId });

            entity.Property(e => e.PortfolioId).HasColumnName("portfolio_id");
            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity").HasPrecision(18, 8).IsRequired();
            entity.Property(e => e.AverageCost).HasColumnName("average_cost").HasPrecision(19, 4).IsRequired();
            entity.Property(e => e.LastUpdated).HasColumnName("last_updated").HasDefaultValueSql("SYSDATETIME()");

            entity.HasOne(e => e.Portfolio)
                .WithMany(e => e.Holdings)
                .HasForeignKey(e => e.PortfolioId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Instrument)
                .WithMany(e => e.Holdings)
                .HasForeignKey(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HistoricalPrice>(entity =>
        {
            entity.ToTable("HISTORICAL_PRICES");
            entity.HasKey(e => e.PriceId);

            entity.Property(e => e.PriceId).HasColumnName("price_id");
            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id");
            entity.Property(e => e.PriceDate).HasColumnName("price_date");
            entity.Property(e => e.OpenPrice).HasColumnName("open_price").HasPrecision(19, 4).IsRequired();
            entity.Property(e => e.HighPrice).HasColumnName("high_price").HasPrecision(19, 4).IsRequired();
            entity.Property(e => e.LowPrice).HasColumnName("low_price").HasPrecision(19, 4).IsRequired();
            entity.Property(e => e.ClosePrice).HasColumnName("close_price").HasPrecision(19, 4).IsRequired();
            entity.Property(e => e.AdjustedClose).HasColumnName("adjusted_close").HasPrecision(19, 4).IsRequired();
            entity.Property(e => e.Volume).HasColumnName("volume");

            entity.HasIndex(e => new { e.InstrumentId, e.PriceDate }).IsUnique();

            entity.HasOne(e => e.Instrument)
                .WithMany(e => e.HistoricalPrices)
                .HasForeignKey(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}
