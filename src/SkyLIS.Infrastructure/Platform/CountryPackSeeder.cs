using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SkyLIS.Domain.Platform;
using SkyLIS.Infrastructure.Persistence;

namespace SkyLIS.Infrastructure.Platform;

/// <summary>
/// P01.4: the canonical country packs ship with the platform. Seeded idempotently at
/// startup (insert-if-missing, never overwrite — operators evolve packs via the Admin
/// Portal, and startup must not roll their edits back).
/// </summary>
internal sealed class CountryPackSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CountryPackSeeder> _logger;

    public CountryPackSeeder(IServiceScopeFactory scopes, ILogger<CountryPackSeeder> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SkyLisDbContext>();

        if (await db.CountryPacks.AnyAsync(p => p.CountryCode == "EG", ct))
            return;

        db.CountryPacks.Add(EgyptPack());
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded the Egypt (EG) country pack.");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static CountryPack EgyptPack() => CountryPack.Create(
        Guid.CreateVersion7(), "EG", "Egypt Defaults", "EGP",
        [
            new PackSampleType("Whole blood (EDTA)", "EDTA (lavender)",
                [new PackCondition("Random", null, "WB-G1")]),
            new PackSampleType("Serum", "SST (gold)",
                [
                    new PackCondition("Random", null, "SR-G1"),
                    new PackCondition("Fasting 8h", null, "SR-G1"),
                    new PackCondition("Post-prandial +2h", 120, "SR-G2"),
                ]),
            new PackSampleType("Urine (random)", "Sterile cup",
                [new PackCondition("Random", null, "UR-G1")]),
            new PackSampleType("Stool", "Stool container",
                [new PackCondition("Random", null, "ST-G1")]),
        ],
        DateTimeOffset.UtcNow);
}
