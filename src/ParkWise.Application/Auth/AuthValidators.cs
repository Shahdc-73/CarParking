using FluentValidation;

namespace ParkWise.Application.Auth;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).Password();
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public static class PasswordRules
{
    /// <summary>Mirrors the Identity password options so users get a 400 with field errors instead of an opaque failure.</summary>
    public static IRuleBuilderOptions<T, string> Password<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty()
        .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
        .MaximumLength(100)
        .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
        .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
        .Matches("[0-9]").WithMessage("Password must contain a digit.");
}
