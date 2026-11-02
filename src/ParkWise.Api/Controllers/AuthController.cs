using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkWise.Api.Infrastructure;
using ParkWise.Application.Auth;
using ParkWise.Application.Common;

namespace ParkWise.Api.Controllers;

[Route("api/v1/auth")]
public class AuthController(IAuthService auth) : ApiControllerBase
{
    private const string RefreshCookie = "pw_refresh";
    private const string CookiePath = "/api/v1/auth"; // the browser only sends it to auth endpoints

    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
        => Respond(await auth.RegisterAsync(request, ct), StatusCodes.Status201Created);

    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
        => Respond(await auth.LoginAsync(request, ct));

    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var result = await auth.RefreshAsync(Request.Cookies[RefreshCookie], ct);
        if (!result.IsSuccess) DeleteRefreshCookie(); // a bad/replayed token must not stay in the browser
        return Respond(result);
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        DeleteRefreshCookie();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct)
        => ToActionResult(await auth.GetCurrentUserAsync(User.GetUserId(), ct));

    private IActionResult Respond(Result<AuthSession> result, int successStatus = StatusCodes.Status200OK)
    {
        if (!result.IsSuccess) return FromError(result.Error!);

        var session = result.Value;
        Response.Cookies.Append(RefreshCookie, session.RefreshToken, new CookieOptions
        {
            HttpOnly = true,                       // JavaScript (and therefore XSS) cannot read it
            Secure = Request.IsHttps,              // HTTPS-only in production; plain http works for local dev
            SameSite = SameSiteMode.Strict,        // never sent on cross-site requests (CSRF defence)
            Path = CookiePath,
            Expires = session.RefreshTokenExpiresUtc,
            IsEssential = true,
        });

        return StatusCode(successStatus, session.Response);
    }

    private void DeleteRefreshCookie()
        => Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = CookiePath, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict, HttpOnly = true });
}
