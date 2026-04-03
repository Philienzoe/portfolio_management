using System.Security.Claims;
using Ipms.Api.Contracts;
using Ipms.Api.Data;
using Ipms.Api.Helpers;
using Ipms.Api.Models;
using Ipms.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ipms.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth").WithTags("Authentication");

        auth.MapPost("/register", Register);
        auth.MapPost("/login", Login);
        auth.MapGet("/me", Me).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        IpmsDbContext db,
        IPasswordHasher<User> passwordHasher,
        IJwtTokenService jwtTokenService,
        CancellationToken cancellationToken)
    {
        var validation = ValidateCredentials(request.Email, request.Password);
        if (validation is not null)
        {
            return validation;
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailInUse = await db.Users.AnyAsync(user => user.Email == normalizedEmail, cancellationToken);
        if (emailInUse)
        {
            return Results.Conflict(new { message = $"The email '{normalizedEmail}' is already registered." });
        }

        var user = new User
        {
            Email = normalizedEmail,
            FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        var token = jwtTokenService.CreateToken(user);
        return Results.Created($"/api/users/{user.UserId}", new AuthResponse(
            "Bearer",
            token.AccessToken,
            token.ExpiresAtUtc,
            user.ToSummary()));
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        IpmsDbContext db,
        IPasswordHasher<User> passwordHasher,
        IJwtTokenService jwtTokenService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { message = "Email and password are required." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(item => item.Email == normalizedEmail, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return Results.Unauthorized();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(cancellationToken);
        }

        var token = jwtTokenService.CreateToken(user);
        return Results.Ok(new AuthResponse(
            "Bearer",
            token.AccessToken,
            token.ExpiresAtUtc,
            user.ToSummary()));
    }

    private static async Task<IResult> Me(
        ClaimsPrincipal currentUser,
        IpmsDbContext db,
        CancellationToken cancellationToken)
    {
        var currentUserId = currentUser.GetCurrentUserId();
        if (currentUserId is null)
        {
            return Results.Unauthorized();
        }

        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == currentUserId, cancellationToken);

        return user is null
            ? Results.NotFound(new { message = "Current user was not found." })
            : Results.Ok(user.ToSummary());
    }

    private static IResult? ValidateCredentials(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Results.BadRequest(new { message = "Email and password are required." });
        }

        if (!email.Contains('@'))
        {
            return Results.BadRequest(new { message = "A valid email address is required." });
        }

        if (password.Length < 8)
        {
            return Results.BadRequest(new { message = "Password must be at least 8 characters long." });
        }

        return null;
    }
}
