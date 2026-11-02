using ParkWise.Application.Common;

namespace ParkWise.Api.Infrastructure;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirst("sub")?.Value, out var id) ? id : null;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}
