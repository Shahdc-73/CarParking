using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ParkWise.Application.Audit;
using ParkWise.Application.Common;
using ParkWise.Application.Parking;
using ParkWise.Application.Vehicles;

namespace ParkWise.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<IAssemblyMarker>(includeInternalTypes: true);

        // Scoped: all of these share the request's single IAppDbContext, so a service call is one unit of work.
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<ILotService, LotService>();
        services.AddScoped<ISpotService, SpotService>();
        services.AddScoped<IVehicleService, VehicleService>();
        return services;
    }
}

/// <summary>Anchor type so other code can say "the Application assembly" without naming a random class.</summary>
public interface IAssemblyMarker;
