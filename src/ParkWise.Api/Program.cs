using Microsoft.EntityFrameworkCore;
using ParkWise.Api.Infrastructure;
using ParkWise.Application;
using ParkWise.Infrastructure;
using ParkWise.Infrastructure.Identity;
using ParkWise.Infrastructure.Persistence;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, config) => config
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddControllers(o => o.Filters.Add<ValidationFilter>())
        // Our ValidationFilter replaces the built-in automatic 400, so errors have one consistent shape.
        .ConfigureApiBehaviorOptions(o => o.SuppressModelStateInvalidFilter = true);
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddJwtAuthentication();
    builder.Services.AddSwaggerWithJwt();

    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials())); // needed so the browser sends the refresh cookie cross-origin

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

    var app = builder.Build();

    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db);
        await IdentitySeeder.SeedAsync(scope.ServiceProvider);
    }

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging();

    if (!app.Environment.IsProduction())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // Liveness: is the process up? No dependencies, so it never flaps because of the database.
    app.MapHealthChecks("/health", new() { Predicate = _ => false });
    // Readiness: can it serve traffic? Includes the database.
    app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Makes Program visible to WebApplicationFactory in the integration tests.
public partial class Program;
