using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkWise.Application.Reservations;

namespace ParkWise.Infrastructure.BackgroundJobs;

public sealed class ReservationJobOptions
{
    public const string Section = "Reservations:ExpiryJob";

    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 30;
}

/// <summary>
/// Runs <see cref="IReservationMaintenance"/> on a timer: expires missed reservations (start + 15 min), starts holds on
/// time, and moves a booking when its spot is unavailable.
///
/// DI lifetime lesson: a hosted service is a SINGLETON for the life of the app, but the DbContext behind the maintenance
/// service is SCOPED. Injecting it directly would capture one DbContext forever (stale data, thread-unsafe). So the worker
/// holds only the scope FACTORY and creates a fresh scope, like a mini HTTP request, for every run.
/// </summary>
public sealed class ReservationExpiryWorker(
    IServiceScopeFactory scopes,
    IOptions<ReservationJobOptions> options,
    TimeProvider clock,
    ILogger<ReservationExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Reservation expiry job is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, settings.IntervalSeconds));
        logger.LogInformation("Reservation expiry job running every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval, clock);
        try
        {
            await RunOnceSafelyAsync(stoppingToken); // catch up immediately after a restart instead of waiting a full interval
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnceSafelyAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }
    }

    /// <summary>An exception in one run (database down, deadlock...) must never kill the loop: log it and try again next tick.</summary>
    private async Task RunOnceSafelyAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IReservationMaintenance>().RunOnceAsync(ct);

            if (result.Expired + result.HoldsStarted + result.Reassigned + result.CancelledNoSpot > 0)
                logger.LogInformation("Reservation job: {Expired} expired, {Started} holds started, {Reassigned} reassigned, {Cancelled} cancelled (no spot).",
                    result.Expired, result.HoldsStarted, result.Reassigned, result.CancelledNoSpot);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reservation job run failed; will retry on the next tick.");
        }
    }
}
