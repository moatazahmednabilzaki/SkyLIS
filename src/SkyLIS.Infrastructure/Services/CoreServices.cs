using Microsoft.EntityFrameworkCore;
using SkyLIS.Application.Common;
using SkyLIS.Infrastructure.Persistence;
using SkyLIS.Infrastructure.Tenancy;

namespace SkyLIS.Infrastructure.Services;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Tenant-scoped, human-facing number series (visit / patient / invoice numbers).
/// Optimistic concurrency on the counter row; retried on conflict.
/// </summary>
internal sealed class NumberSeriesService : INumberSeriesService
{
    private const int MaxRetries = 5;
    private readonly SkyLisDbContext _db;
    private readonly TenantContext _tenant;
    private readonly IClock _clock;

    public NumberSeriesService(SkyLisDbContext db, TenantContext tenant, IClock clock)
    {
        _db = db;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<string> NextAsync(string seriesKind, string? scope = null, CancellationToken ct = default)
    {
        var prefix = seriesKind switch
        {
            "visit" => "V",
            "patient" => "PN",
            "invoice" => "INV",
            "report" => "R",
            _ => throw new ArgumentException($"Unknown number series '{seriesKind}'.", nameof(seriesKind)),
        };
        // Scoped series (per branch, P03.2) run their own counter and embed the scope code.
        var kind = scope is null ? seriesKind : $"{seriesKind}:{scope}";

        for (var attempt = 0; ; attempt++)
        {
            var series = await _db.NumberSeries
                .FirstOrDefaultAsync(n => n.Kind == kind, ct);
            if (series is null)
            {
                series = new NumberSeries
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = _tenant.TenantId,
                    Kind = kind,
                    LastValue = 0,
                };
                _db.NumberSeries.Add(series);
            }

            series.LastValue++;
            try
            {
                // The counter must advance independently of the business transaction outcome
                // to avoid long lock windows; a gap on rollback is acceptable by design.
                await _db.SaveChangesAsync(ct);
                var stamp = _clock.UtcNow.ToString("yyMMdd");
                return scope is null
                    ? $"{prefix}-{stamp}-{series.LastValue:D4}"
                    : $"{prefix}-{scope}-{stamp}-{series.LastValue:D4}";
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxRetries)
            {
                foreach (var entry in _db.ChangeTracker.Entries<NumberSeries>())
                    await entry.ReloadAsync(ct);
            }
        }
    }
}
