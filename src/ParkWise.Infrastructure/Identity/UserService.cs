using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Auth;
using ParkWise.Application.Common;
using ParkWise.Application.Users;
using ParkWise.Infrastructure.Persistence;

namespace ParkWise.Infrastructure.Identity;

public sealed class UserService(UserManager<ApplicationUser> users, AppDbContext db, TimeProvider clock, ICurrentUser currentUser) : IUserService
{
    public async Task<PagedResult<UserDto>> ListAsync(UserListQuery query, CancellationToken ct)
    {
        var q = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(u => u.Email!.Contains(term) || u.FullName.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            q = q.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id &&
                db.Roles.Any(r => r.Id == ur.RoleId && r.Name == query.Role)));
        }

        q = query.Sort?.ToLowerInvariant() switch
        {
            "email_desc" => q.OrderByDescending(u => u.Email),
            "name" => q.OrderBy(u => u.FullName),
            "name_desc" => q.OrderByDescending(u => u.FullName),
            _ => q.OrderBy(u => u.Email),
        };

        var paging = query.Paging;
        var total = await q.CountAsync(ct);
        var page = await q.Skip(paging.Skip).Take(paging.SafePageSize).ToListAsync(ct);

        var ids = page.Select(u => u.Id).ToList();
        var roleLookup = await (from ur in db.UserRoles
                                join r in db.Roles on ur.RoleId equals r.Id
                                where ids.Contains(ur.UserId)
                                select new { ur.UserId, r.Name })
            .ToListAsync(ct);

        var items = page.Select(u => new UserDto(
            u.Id, u.Email!, u.FullName,
            roleLookup.Where(x => x.UserId == u.Id).Select(x => x.Name!).OrderBy(n => n).ToList(),
            u.IsActive)).ToList();

        return new PagedResult<UserDto>(items, paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<Result<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            FullName = request.FullName.Trim(),
            EmailConfirmed = true,
        };

        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            var duplicate = created.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
            return duplicate
                ? Error.Conflict("users.email_taken", "An account with this email already exists.")
                : Error.Validation("users.invalid", string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(user, request.Role);
        return new UserDto(user.Id, user.Email!, user.FullName, [request.Role], user.IsActive);
    }

    public async Task<Result> SetActiveAsync(Guid userId, bool isActive, CancellationToken ct)
    {
        // Deactivating yourself would lock you out, and if you are the last admin nobody could undo it.
        if (!isActive && currentUser.UserId == userId)
            return Result.Failure(Error.Conflict("users.cannot_deactivate_self", "You cannot deactivate your own account."));

        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return Result.Failure(Error.NotFound("users.not_found", "User not found."));

        user.IsActive = isActive;
        await users.UpdateAsync(user);

        if (!isActive) // deactivating must also kill existing sessions
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, now), ct);
        }

        return Result.Success();
    }
}
