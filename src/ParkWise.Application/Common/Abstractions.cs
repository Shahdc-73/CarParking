using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ParkWise.Domain.Entities;

namespace ParkWise.Application.Common;

/// <summary>
/// What the Application layer needs from the database. Application knows EF Core's DbSet/LINQ (a deliberate,
/// pragmatic choice: no repository boilerplate), but not SQL Server or the concrete DbContext.
/// </summary>
public interface IAppDbContext
{
    DbSet<ParkingLot> Lots { get; }
    DbSet<Level> Levels { get; }
    DbSet<Spot> Spots { get; }
    DbSet<Vehicle> Vehicles { get; }
    DbSet<ParkingSession> Sessions { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<PricingRule> PricingRules { get; }
    DbSet<LostTicketFee> LostTicketFees { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Receipt> Receipts { get; }

    /// <summary>Exposed so a service can discard a failed unit of work and retry from a clean slate.</summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>The authenticated caller, so services can enforce ownership without touching HttpContext.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsInRole(string role);
}

public static class CurrentUserExtensions
{
    public static bool IsStaff(this ICurrentUser user)
        => user.IsInRole(Auth.Roles.Admin) || user.IsInRole(Auth.Roles.Attendant);
}

/// <summary>
/// Adds an audit record to the current unit of work. It does NOT save: the caller's single SaveChanges
/// commits the change and its audit entry in ONE transaction, so you can never have one without the other.
/// </summary>
public interface IAuditLogger
{
    void Log(string action, string entityType, Guid entityId, object? before = null, object? after = null);
}

public sealed record SetActiveRequest(bool IsActive);
