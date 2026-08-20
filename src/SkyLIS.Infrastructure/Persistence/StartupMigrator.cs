using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SkyLIS.Infrastructure.Persistence;

/// <summary>
/// On-prem startup migration (Database:MigrateOnStartup=true): applies pending EF
/// migrations and re-applies the Row-Level Security policies (idempotent script,
/// embedded in the binary) before any other hosted service touches the database.
/// Development keeps explicit `dotnet ef database update` — the flag defaults to off.
/// Single-node semantics by design; multi-node deployments run migrations as a
/// dedicated job step instead.
/// </summary>
internal sealed class StartupMigrator : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StartupMigrator> _logger;

    public StartupMigrator(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<StartupMigrator> logger)
    {
        _scopes = scopes;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!string.Equals(_configuration["Database:MigrateOnStartup"], "true", StringComparison.OrdinalIgnoreCase))
            return;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SkyLisDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count > 0)
        {
            _logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync(ct);
        }

        // RLS is enforced with FORCE, so even the table owner (the application role that
        // ran the migrations) is subject to the tenant policies.
        var assembly = typeof(StartupMigrator).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("enable-rls.sql", StringComparison.OrdinalIgnoreCase));
        await using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(ct);
        await db.Database.ExecuteSqlRawAsync(sql, ct);
        _logger.LogInformation("Row-Level Security policies applied.");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
