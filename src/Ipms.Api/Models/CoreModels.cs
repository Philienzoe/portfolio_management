namespace Ipms.Api.Models;

public sealed class User
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Portfolio> Portfolios { get; set; } = [];
}

public sealed class Portfolio
{
    public int PortfolioId { get; set; }
    public int UserId { get; set; }
    public string PortfolioName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public List<TransactionRecord> Transactions { get; set; } = [];
    public List<PortfolioHolding> Holdings { get; set; } = [];
}

public sealed class StockExchange
{
    public int ExchangeId { get; set; }
    public string MicCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;

    public List<Stock> Stocks { get; set; } = [];
    public List<Etf> Etfs { get; set; } = [];
}
