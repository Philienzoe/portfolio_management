using System.Text;
using System.Text.Json.Serialization;
using Ipms.Api.Contracts;
using Ipms.Api.Data;
using Ipms.Api.Endpoints;
using Ipms.Api.Models;
using Ipms.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
    ?? throw new InvalidOperationException("Missing Auth configuration.");
var marketDataOptions = builder.Configuration.GetSection(MarketDataOptions.SectionName).Get<MarketDataOptions>()
    ?? throw new InvalidOperationException("Missing MarketData configuration.");

if (string.IsNullOrWhiteSpace(authOptions.SecretKey) || authOptions.SecretKey.Length < 32)
{
    throw new InvalidOperationException("The Auth secret key must be at least 32 characters long.");
}

if (string.IsNullOrWhiteSpace(marketDataOptions.BaseUrl))
{
    throw new InvalidOperationException("MarketData BaseUrl is required.");
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<MarketDataOptions>(builder.Configuration.GetSection(MarketDataOptions.SectionName));
builder.Services.AddDbContext<IpmsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("IpmsDatabase")));
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddSingleton<MarketDataRefreshState>();
builder.Services.AddHttpClient<IMarketDataService, YahooFinanceMarketDataService>((_, client) =>
{
    client.BaseAddress = new Uri(marketDataOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHostedService<MarketDataRefreshHostedService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SecretKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorizationBuilder();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

var api = app.MapGroup("/api");

api.MapGet("/health", async (IpmsDbContext db, CancellationToken cancellationToken) =>
{
    var canConnect = await db.Database.CanConnectAsync(cancellationToken);
    var connection = db.Database.GetDbConnection();

    return Results.Ok(new HealthResponse(
        canConnect ? "Healthy" : "Unhealthy",
        connection.Database,
        connection.DataSource,
        DateTime.UtcNow));
})
.WithName("GetHealth")
.WithTags("System");

api.MapAuthEndpoints();
api.MapUserEndpoints();
api.MapPortfolioEndpoints();
api.MapInstrumentEndpoints();
api.MapAnalyticsEndpoints();
api.MapMarketDataEndpoints();

app.Run();

public partial class Program;
