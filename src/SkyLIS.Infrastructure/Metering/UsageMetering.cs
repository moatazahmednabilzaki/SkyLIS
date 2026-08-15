using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SkyLIS.Application.Common;
using SkyLIS.Infrastructure.Persistence;

namespace SkyLIS.Infrastructure.Metering;

/// <summary>
/// Platform-scoped monthly usage counter (FR-SYS-011). Lives in the platform schema
/// (no RLS): written by the outbox dispatcher, read by the Admin Portal (P01.3).
/// </summary>
public sealed class UsageMeter
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public int Year { get; init; }
    public int Month { get; init; }
    public int FinalizedReports { get; init; }
}

internal sealed class UsageMeterConfig : IEntityTypeConfiguration<UsageMeter>
{
    public void Configure(EntityTypeBuilder<UsageMeter> b)
    {
        b.ToTable("usage_meters", "platform");
        b.HasKey(m => m.Id);
        b.HasIndex(m => new { m.TenantId, m.Year, m.Month }).IsUnique();
    }
}

internal sealed class UsageMeterStore : IUsageMeterStore
{
    private readonly SkyLisDbContext _db;
    public UsageMeterStore(SkyLisDbContext db) => _db = db;

    public Task IncrementFinalizedReportsAsync(Guid tenantId, DateTimeOffset occurredAtUtc, CancellationToken ct = default)
    {
        // Atomic upsert; joins the dispatcher's ambient transaction, so the increment
        // commits together with the inbox row (idempotent under redelivery).
        var id = Guid.CreateVersion7();
        var year = occurredAtUtc.Year;
        var month = occurredAtUtc.Month;
        return _db.Database.ExecuteSqlAsync($@"
            INSERT INTO platform.usage_meters (id, tenant_id, year, month, finalized_reports)
            VALUES ({id}, {tenantId}, {year}, {month}, 1)
            ON CONFLICT (tenant_id, year, month)
            DO UPDATE SET finalized_reports = platform.usage_meters.finalized_reports + 1", ct);
    }

    public async Task<IReadOnlyList<UsageMeterDto>> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.Set<UsageMeter>().AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .OrderByDescending(m => m.Year).ThenByDescending(m => m.Month)
            .Select(m => new UsageMeterDto(m.TenantId, m.Year, m.Month, m.FinalizedReports))
            .ToListAsync(ct);
}
