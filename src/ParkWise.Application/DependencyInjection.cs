using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParkWise.Application.Audit;
using ParkWise.Application.Common;
using ParkWise.Application.Parking;
using ParkWise.Application.Pricing;
using ParkWise.Application.Reports;
using ParkWise.Application.Reservations;
using ParkWise.Application.Sessions;
using ParkWise.Application.Vehicles;

namespace ParkWise.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<IAssemblyMarker>(includeInternalTypes: true);

        // A real notifier (SignalR) is registered by the Api project and wins; this keeps the services usable without one.
        services.TryAddSingleton<IAvailabilityNotifier, NullAvailabilityNotifier>();

        // Scoped: all of these share the request's single IAppDbContext, so a service call is one unit of work.
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<ILotService, LotService>();
        services.AddScoped<ISpotService, SpotService>();
        services.AddScoped<ILotBoardService, LotBoardService>();
        services.AddScoped<IVehicleService, VehicleService>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IReservationMaintenance, ReservationMaintenance>();
        return services;
    }
}

/// <summary>Anchor type so other code can say "the Application assembly" without naming a random class.</summary>
public interface IAssemblyMarker;
