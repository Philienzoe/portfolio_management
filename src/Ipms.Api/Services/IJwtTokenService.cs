using Ipms.Api.Models;

namespace Ipms.Api.Services;

public sealed record JwtTokenResult(
    string AccessToken,
    DateTime ExpiresAtUtc);

public interface IJwtTokenService
{
    JwtTokenResult CreateToken(User user);
}
