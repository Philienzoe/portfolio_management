namespace Ipms.Api.Services;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Issuer { get; init; } = "Ipms.Api";
    public string Audience { get; init; } = "Ipms.Client";
    public string SecretKey { get; init; } = string.Empty;
    public int TokenLifetimeMinutes { get; init; } = 120;
}
