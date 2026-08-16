using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkyLIS.Domain.Platform;
using SkyLIS.Infrastructure.Persistence;

namespace SkyLIS.Infrastructure.Platform;

/// <summary>
/// P01.3: the canonical Egypt plans (LIS_Subscription_Plans_Egypt) ship with the platform.
/// Seeded insert-if-missing; operators evolve them via the Admin Portal plan builder.
/// </summary>
internal sealed class PlanSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PlanSeeder> _logger;

    public PlanSeeder(IServiceScopeFactory scopes, ILogger<PlanSeeder> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SkyLisDbContext>();

        var canonical = new (string Code, string Name, decimal Price, int Users, int Branches, int Reports)[]
        {
            ("LITE", "Sky LIS Lite", 999m, 2, 1, 300),
            ("STARTER", "Sky LIS Starter", 2499m, 5, 1, 1000),
            ("PROFESSIONAL", "Sky LIS Professional", 5999m, 15, 3, 5000),
            ("ENTERPRISE", "Sky LIS Enterprise", 14999m, 50, 10, 25000),
        };

        var existing = await db.Plans.Select(p => p.Code).ToListAsync(ct);
        var seeded = 0;
        foreach (var plan in canonical.Where(p => !existing.Contains(p.Code)))
        {
            db.Plans.Add(Plan.Create(
                Guid.CreateVersion7(), plan.Code, plan.Name, plan.Price, "EGP",
                plan.Users, plan.Branches, plan.Reports));
            seeded++;
        }
        if (seeded > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} subscription plan(s).", seeded);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
