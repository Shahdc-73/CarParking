using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkWise.Api.Infrastructure;
using ParkWise.Application.Auth;
using ParkWise.Application.Common;
using ParkWise.Application.Users;

namespace ParkWise.Api.Controllers;

[Route("api/v1/users")]
[Authorize(Roles = Roles.Admin)]
public class UsersController(IUserService users) : ApiControllerBase
{
    public sealed record SetActiveRequest(bool IsActive);

    [HttpGet]
    public async Task<ActionResult<PagedResult<UserDto>>> List([FromQuery] UserListQuery query, CancellationToken ct)
        => Ok(await users.ListAsync(query, ct));

    [HttpPost]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct)
        => ToActionResult(await users.CreateAsync(request, ct), dto => StatusCode(StatusCodes.Status201Created, dto));

    [HttpPut("{id:guid}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetActive(Guid id, SetActiveRequest request, CancellationToken ct)
        => ToActionResult(await users.SetActiveAsync(id, request.IsActive, ct));
}
