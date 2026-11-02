using ParkWise.Application.Common;

namespace ParkWise.Application.Auth;

public sealed record RegisterRequest(string Email, string Password, string FullName);

public sealed record LoginRequest(string Email, string Password);

public sealed record UserDto(Guid Id, string Email, string FullName, IReadOnlyList<string> Roles, bool IsActive);

/// <summary>What the browser gets in the JSON body. The refresh token is NOT here; it travels in an HttpOnly cookie.</summary>
public sealed record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, UserDto User);

/// <summary>Internal result: the API layer splits this into body (AuthResponse) and cookie (RefreshToken).</summary>
public sealed record AuthSession(AuthResponse Response, string RefreshToken, DateTime RefreshTokenExpiresUtc);

public interface IAuthService
{
    Task<Result<AuthSession>> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<Result<AuthSession>> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<Result<AuthSession>> RefreshAsync(string? refreshToken, CancellationToken ct);
    Task<Result> LogoutAsync(string? refreshToken, CancellationToken ct);
    Task<Result<UserDto>> GetCurrentUserAsync(Guid userId, CancellationToken ct);
}
