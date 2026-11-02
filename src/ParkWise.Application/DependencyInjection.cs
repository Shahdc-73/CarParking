using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ParkWise.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<IAssemblyMarker>(includeInternalTypes: true);
        return services;
    }
}

/// <summary>Anchor type so other code can say "the Application assembly" without naming a random class.</summary>
public interface IAssemblyMarker;
