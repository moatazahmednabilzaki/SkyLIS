using Microsoft.EntityFrameworkCore;
using SkyLIS.Application.Catalog;
using SkyLIS.Application.Org;
using SkyLIS.Application.Platform;

namespace SkyLIS.Infrastructure.Persistence;

internal sealed class BranchQueries : IBranchQueries
{
    private readonly SkyLisDbContext _db;
    public BranchQueries(SkyLisDbContext db) => _db = db;

    public async Task<IReadOnlyList<BranchDto>> ListAsync(CancellationToken ct = default) =>
        await _db.Branches.AsNoTracking()
            .OrderByDescending(b => b.IsMain).ThenBy(b => b.Code)
            .Select(b => new BranchDto(
                b.Id, b.Code, b.Name, b.Address, b.Phone, b.IsMain, b.IsActive,
                b.Departments.OrderBy(d => d.Code)
                    .Select(d => new DepartmentDto(d.Id, d.Code, d.Name)).ToList()))
            .ToListAsync(ct);
}

internal sealed class CountryPackQueries : ICountryPackQueries
{
    private readonly SkyLisDbContext _db;
    public CountryPackQueries(SkyLisDbContext db) => _db = db;

    public async Task<IReadOnlyList<CountryPackDto>> ListAsync(CancellationToken ct = default)
    {
        // The jsonb content is materialized via the value converter; shape client-side.
        var packs = await _db.CountryPacks.AsNoTracking().OrderBy(p => p.CountryCode).ToListAsync(ct);
        return packs.Select(p => new CountryPackDto(
            p.Id, p.CountryCode, p.Name, p.Currency, p.Version, p.UpdatedAtUtc,
            p.SampleTypes.Select(s => new PackSampleTypeDto(
                s.Name, s.ContainerName,
                s.Conditions.Select(c => new PackConditionDto(c.Name, c.DelayMinutes, c.CompatibilityGroup)).ToList()))
                .ToList())).ToList();
    }
}

internal sealed class CatalogQueries : ICatalogQueries
{
    private readonly SkyLisDbContext _db;
    public CatalogQueries(SkyLisDbContext db) => _db = db;

    public async Task<IReadOnlyList<SampleTypeListDto>> ListSampleTypesAsync(CancellationToken ct = default) =>
        await _db.SampleTypes.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SampleTypeListDto(
                s.Id, s.Name, s.ContainerName,
                s.Conditions.OrderBy(c => c.Name)
                    .Select(c => new ConditionDto(c.Id, c.Name, c.DelayMinutes, c.CompatibilityGroup)).ToList()))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TestListDto>> ListTestsAsync(string? status, CancellationToken ct = default)
    {
        var query = _db.LabTests.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status.ToString() == status);
        return await query
            .OrderBy(t => t.Code)
            .Select(t => new TestListDto(
                t.Id, t.Code, t.Name, t.Department, t.Status.ToString(), t.Origin.ToString(),
                t.Price == null ? null : t.Price.Amount,
                t.Price == null ? null : t.Price.Currency,
                t.SampleTypeId, t.RequiredConditionId,
                t.ResultSchema != null))
            .ToListAsync(ct);
    }
}
