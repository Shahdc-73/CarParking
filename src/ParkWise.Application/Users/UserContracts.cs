using FluentValidation;
using ParkWise.Application.Auth;
using ParkWise.Application.Common;

namespace ParkWise.Application.Users;

public sealed record CreateUserRequest(string Email, string Password, string FullName, string Role);

public sealed record UserListQuery(string? Search, string? Role, string? Sort, int Page = 1, int PageSize = 20)
{
    public PageQuery Paging => new(Page, PageSize);
}

public interface IUserService
{
    Task<PagedResult<UserDto>> ListAsync(UserListQuery query, CancellationToken ct);
    Task<Result<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken ct);
    Task<Result> SetActiveAsync(Guid userId, bool isActive, CancellationToken ct);
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).Password();
        RuleFor(x => x.Role).Must(r => Roles.All.Contains(r)).WithMessage($"Role must be one of: {string.Join(", ", Roles.All)}.");
    }
}
