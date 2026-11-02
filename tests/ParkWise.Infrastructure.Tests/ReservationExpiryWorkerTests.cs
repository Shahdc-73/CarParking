using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkWise.Application.Reservations;
using ParkWise.Infrastructure.BackgroundJobs;

namespace ParkWise.Infrastructure.Tests;

/// <summary>The worker is just a timer loop; these tests prove the loop behaves, using a fake clock instead of real waiting.</summary>
public class ReservationExpiryWorkerTests
{
    private sealed class Harness : IAsyncDisposable
    {
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        public IReservationMaintenance Maintenance { get; } = Substitute.For<IReservationMaintenance>();
        public List<object> ScopesSeen { get; } = [];
        public ReservationExpiryWorker Worker { get; }

        public Harness(bool enabled = true, int intervalSeconds = 30)
        {
            Maintenance.RunOnceAsync(Arg.Any<CancellationToken>()).Returns(new MaintenanceResult(0, 0, 0, 0));

            // A new object per scope proves the worker creates a fresh scope on every run (the DbContext lifetime lesson).
            var services = new ServiceCollection();
            services.AddScoped(_ => { var marker = new object(); lock (ScopesSeen) ScopesSeen.Add(marker); return marker; });
            services.AddScoped(sp => { _ = sp.GetRequiredService<object>(); return Maintenance; });
            var provider = services.BuildServiceProvider();

            Worker = new ReservationExpiryWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new ReservationJobOptions { Enabled = enabled, IntervalSeconds = intervalSeconds }),
                Clock,
                NullLogger<ReservationExpiryWorker>.Instance);
        }

        public int Runs => Maintenance.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IReservationMaintenance.RunOnceAsync));

        public async Task WaitForRunsAsync(int expected)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Runs < expected && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.True(Runs >= expected, $"expected {expected} runs but saw {Runs}");
        }

        public async ValueTask DisposeAsync()
        {
            await Worker.StopAsync(CancellationToken.None);
            Worker.Dispose();
        }
    }

    [Fact]
    public async Task Runs_immediately_on_start_to_catch_up_after_a_restart()
    {
        await using var h = new Harness();
        await h.Worker.StartAsync(CancellationToken.None);

        await h.WaitForRunsAsync(1);
        Assert.Equal(1, h.Runs);
    }

    [Fact]
    public async Task Runs_again_once_per_interval()
    {
        await using var h = new Harness(intervalSeconds: 30);
        await h.Worker.StartAsync(CancellationToken.None);
        await h.WaitForRunsAsync(1);

        h.Clock.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(100);
        Assert.Equal(1, h.Runs);          // not yet

        h.Clock.Advance(TimeSpan.FromSeconds(1));
        await h.WaitForRunsAsync(2);

        h.Clock.Advance(TimeSpan.FromSeconds(30));
        await h.WaitForRunsAsync(3);
    }

    [Fact]
    public async Task Every_run_gets_its_own_scope()
    {
        await using var h = new Harness();
        await h.Worker.StartAsync(CancellationToken.None);
        await h.WaitForRunsAsync(1);
        h.Clock.Advance(TimeSpan.FromSeconds(30));
        await h.WaitForRunsAsync(2);
        h.Clock.Advance(TimeSpan.FromSeconds(30));
        await h.WaitForRunsAsync(3);

        Assert.Equal(3, h.ScopesSeen.Distinct().Count());
    }

    [Fact]
    public async Task A_failing_run_does_not_stop_the_loop()
    {
        await using var h = new Harness();
        h.Maintenance.RunOnceAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MaintenanceResult>(new InvalidOperationException("database is down")),
                     Task.FromResult(new MaintenanceResult(1, 0, 0, 0)));

        await h.Worker.StartAsync(CancellationToken.None);
        await h.WaitForRunsAsync(1);

        h.Clock.Advance(TimeSpan.FromSeconds(30));
        await h.WaitForRunsAsync(2);   // it kept going after the exception
    }

    [Fact]
    public async Task Does_nothing_when_disabled()
    {
        await using var h = new Harness(enabled: false);
        await h.Worker.StartAsync(CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(100);

        Assert.Equal(0, h.Runs);
    }

    [Fact]
    public async Task Stops_promptly_when_the_host_shuts_down()
    {
        await using var h = new Harness();
        await h.Worker.StartAsync(CancellationToken.None);
        await h.WaitForRunsAsync(1);

        var stop = h.Worker.StopAsync(CancellationToken.None);

        Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5))));
        await stop;
    }
}
