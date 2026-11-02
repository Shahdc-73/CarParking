using FluentValidation;

namespace ParkWise.Application.Parking;

public sealed class SaveLotRequestValidator : AbstractValidator<SaveLotRequest>
{
    public SaveLotRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(300);
    }
}

public sealed class CreateLevelRequestValidator : AbstractValidator<CreateLevelRequest>
{
    public CreateLevelRequestValidator()
    {
        RuleFor(x => x.Number).InclusiveBetween(-10, 200); // negatives = basements
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
    }
}

public sealed class UpdateLevelRequestValidator : AbstractValidator<UpdateLevelRequest>
{
    public UpdateLevelRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
}

internal static class SpotRules
{
    public static IRuleBuilderOptions<T, string> SpotCode<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty()
        .Matches("^[A-Za-z0-9-]{1,20}$").WithMessage("Spot code may only contain letters, digits and '-' (max 20).");
}

public sealed class CreateSpotRequestValidator : AbstractValidator<CreateSpotRequest>
{
    public CreateSpotRequestValidator()
    {
        RuleFor(x => x.Code).SpotCode();
        RuleFor(x => x.Type).IsInEnum();
    }
}

public sealed class UpdateSpotRequestValidator : AbstractValidator<UpdateSpotRequest>
{
    public UpdateSpotRequestValidator()
    {
        RuleFor(x => x.Code).SpotCode();
        RuleFor(x => x.Type).IsInEnum();
    }
}
