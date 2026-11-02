using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParkWise.Application.Auth;
using ParkWise.Application.Common;
using ParkWise.Infrastructure.Persistence;

namespace ParkWise.Infrastructure.Identity;

/// <summary>
/// Lives in Infrastructure because it is built on ASP.NET Identity (UserManager) and EF Core.
/// The Application layer only knows the IAuthService contract.
/// </summary>
public sealed class AuthService(
    UserManager<ApplicationUser> users,
    AppDbContext db,
    IJwtTokenService tokens,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider clock) : IAuthService
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Invalid email or password.");
    private static readonly Error InvalidRefresh =
        Error.Unauthorized("auth.invalid_refresh_token", "The refresh token is invalid or expired.");

    public async Task<Result<AuthSession>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            FullName = request.FullName.Trim(),
            EmailConfirmed = true, // no email verification flow in this project; documented trade-off
        };

        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            var duplicate = created.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
            return duplicate
                ? Error.Conflict("auth.email_taken", "An account with this email already exists.")
                : Error.Validation("auth.invalid_registration", string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        // Self-registration can only ever produce a Driver. Staff accounts are created by an Admin.
        await users.AddToRoleAsync(user, Roles.Driver);
        return await IssueSessionAsync(user, familyId: Guid.NewGuid(), ct);
    }

    public async Task<Result<AuthSession>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());

        // Same error for "no such user" and "wrong password" so attackers cannot enumerate accounts.
        if (user is null || !user.IsActive) return InvalidCredentials;

        if (await users.IsLockedOutAsync(user))
            return Error.Unauthorized("auth.locked_out", "Account temporarily locked after too many failed attempts. Try again later.");

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user); // counts toward lockout
            return InvalidCredentials;
        }

        await users.ResetAccessFailedCountAsync(user);
        return await IssueSessionAsync(user, familyId: Guid.NewGuid(), ct);
    }

    public async Task<Result<AuthSession>> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return InvalidRefresh;

        var now = clock.GetUtcNow().UtcDateTime;
        var hash = tokens.Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return InvalidRefresh;

        if (stored.IsRevoked)
        {
            // A token that was already rotated is being replayed: it was probably stolen.
            // Kill the whole family so both the thief and the real user must log in again.
            await RevokeFamilyAsync(stored.FamilyId, now, ct);
            return InvalidRefresh;
        }

        if (stored.IsExpired(now)) return InvalidRefresh;

        var user = await users.FindByIdAsync(stored.UserId.ToString());
        if (user is null || !user.IsActive) return InvalidRefresh;

        var (session, next) = await BuildSessionAsync(user, stored.FamilyId, ct);
        stored.Revoke(now, replacedBy: next.Id);
        db.RefreshTokens.Add(next);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two requests rotated the same token at once; the rowversion let only one win.
            return InvalidRefresh;
        }

        return session;
    }

    public async Task<Result> LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return Result.Success();

        var hash = tokens.Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is not null)
            await RevokeFamilyAsync(stored.FamilyId, clock.GetUtcNow().UtcDateTime, ct);

        return Result.Success(); // idempotent: logging out twice is not an error
    }

    public async Task<Result<UserDto>> GetCurrentUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive) return Error.NotFound("auth.user_not_found", "User not found.");
        return await ToDtoAsync(user);
    }

    private async Task<Result<AuthSession>> IssueSessionAsync(ApplicationUser user, Guid familyId, CancellationToken ct)
    {
        var (session, stored) = await BuildSessionAsync(user, familyId, ct);
        db.RefreshTokens.Add(stored);
        await db.SaveChangesAsync(ct);
        return session;
    }

    private async Task<(AuthSession Session, RefreshToken Stored)> BuildSessionAsync(ApplicationUser user, Guid familyId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var roles = await users.GetRolesAsync(user);
        var (accessToken, accessExpires) = tokens.CreateAccessToken(user, roles);

        var raw = tokens.GenerateRefreshToken();
        var refreshExpires = now.AddDays(jwtOptions.Value.RefreshTokenDays);
        var stored = new RefreshToken(user.Id, familyId, tokens.Hash(raw), now, refreshExpires);

        var dto = new UserDto(user.Id, user.Email!, user.FullName, roles.ToList(), user.IsActive);
        return (new AuthSession(new AuthResponse(accessToken, accessExpires, dto), raw, refreshExpires), stored);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken ct)
        => await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, now), ct);

    private async Task<UserDto> ToDtoAsync(ApplicationUser user)
        => new(user.Id, user.Email!, user.FullName, (await users.GetRolesAsync(user)).ToList(), user.IsActive);
}
