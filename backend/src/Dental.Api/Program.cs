using Dental.Api.Endpoints;
using Dental.Api.Infrastructure;
using Dental.Infrastructure;
using Dental.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;

// Команды: migrate | seed [--reset] | rebuild-balances | worker ; без аргументов — HTTP API.
string[] knownCommands = ["migrate", "seed", "rebuild-balances", "worker"];
var command = args.FirstOrDefault(a => knownCommands.Contains(a));
var hostArgs = command is null ? args : args.Where(a => a != command && a != "--reset").ToArray();

Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture).CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(hostArgs);
    builder.AddDentalApi();
    if (command == "worker") builder.AddDentalWorker();
    else if (command is null) builder.AddDentalBackground();

    var app = builder.Build();

    switch (command)
    {
        case "migrate":
        {
            var db = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            await DatabaseBootstrap.MigrateAsync(db.OwnerConnectionString, db.ConnectionString, app.Logger, CancellationToken.None);
            return 0;
        }
        case "seed":
        {
            await Dental.Seed.DemoSeeder.RunAsync(app.Services, reset: args.Contains("--reset"), CancellationToken.None);
            return 0;
        }
        case "rebuild-balances":
        {
            await Dental.Api.Infrastructure.Maintenance.RebuildBalancesAsync(app.Services, CancellationToken.None);
            return 0;
        }
        case "worker":
            break;
        case null:
            break;
    }

    app.UseDentalApi();
    app.MapOpenApi();
    if (app.Environment.IsDevelopment())
    {
        app.MapScalarApiReference(o => o.WithTitle("Dental Admin API"));
    }
    app.MapSystemEndpoints();
    app.MapDentalEndpoints();
    if (command == "worker") app.UseDentalWorkerDashboard();

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
