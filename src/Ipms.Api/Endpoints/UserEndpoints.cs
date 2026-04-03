using System.Security.Claims;
using Ipms.Api.Contracts;
using Ipms.Api.Data;
using Ipms.Api.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Ipms.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/users").WithTags("Users").RequireAuthorization();

        users.MapGet("/", async (IpmsDbContext db, CancellationToken cancellationToken) =>
        {
            var result = await db.Users
                .AsNoTracking()
                .OrderBy(user => user.UserId)
                .Select(user => new UserSummaryResponse(
                    user.UserId,
                    user.Email,
                    user.FirstName,
                    user.LastName,
                    user.CreatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(result);
        });

        users.MapGet("/{userId:int}", async (
            int userId,
            ClaimsPrincipal currentUser,
            IpmsDbContext db,
            CancellationToken cancellationToken) =>
        {
            var access = EndpointSecurity.EnsureUserAccess(currentUser, userId);
            if (access is not null)
            {
                return access;
            }

            var user = await db.Users
                .AsNoTracking()
                .Include(item => item.Portfolios)
                .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

            return user is null
                ? Results.NotFound(new { message = $"User {userId} was not found." })
                : Results.Ok(user.ToDetail());
        });

        return app;
    }
}
