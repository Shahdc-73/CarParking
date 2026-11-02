using System.Security.Claims;

namespace ParkWise.Api.Infrastructure;

public static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : throw new InvalidOperationException("Authenticated user has no 'sub' claim.");
}
