using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Ipms.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ipms.Api.Helpers;

public static class EndpointSecurity
{
    public static int? GetCurrentUserId(this ClaimsPrincipal user)
    {
        var rawValue = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return int.TryParse(rawValue, out var userId) ? userId : null;
    }

    public static IResult? EnsureUserAccess(ClaimsPrincipal user, int requestedUserId)
    {
        var currentUserId = user.GetCurrentUserId();
        if (currentUserId is null)
        {
            return Results.Unauthorized();
        }

        return currentUserId == requestedUserId
            ? null
            : Results.Forbid();
    }

    public static async Task<IResult?> EnsurePortfolioAccessAsync(
        ClaimsPrincipal user,
        IpmsDbContext db,
        int portfolioId,
        CancellationToken cancellationToken)
    {
        var currentUserId = user.GetCurrentUserId();
        if (currentUserId is null)
        {
            return Results.Unauthorized();
        }

        var ownerId = await db.Portfolios
            .Where(portfolio => portfolio.PortfolioId == portfolioId)
            .Select(portfolio => (int?)portfolio.UserId)
            .SingleOrDefaultAsync(cancellationToken);

        if (ownerId is null)
        {
            return Results.NotFound(new { message = $"Portfolio {portfolioId} was not found." });
        }

        return ownerId == currentUserId
            ? null
            : Results.Forbid();
    }
}
