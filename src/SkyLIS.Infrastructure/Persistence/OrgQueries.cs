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

internal sealed class TenantSettingRepository : ITenantSettingRepository
{
    private readonly SkyLisDbContext _db;
    public TenantSettingRepository(SkyLisDbContext db) => _db = db;

    public Task<Domain.Org.TenantSetting?> GetByKeyAsync(string key, CancellationToken ct = default) =>
        _db.TenantSettings.FirstOrDefaultAsync(s => s.Key == key, ct);

    public void Add(Domain.Org.TenantSetting setting) => _db.TenantSettings.Add(setting);
}

internal sealed class TenantSettingQueries : ITenantSettingQueries
{
    private readonly SkyLisDbContext _db;
    public TenantSettingQueries(SkyLisDbContext db) => _db = db;

    public async Task<IReadOnlyList<TenantSettingDto>> ListAsync(CancellationToken ct = default) =>
        await _db.TenantSettings.AsNoTracking()
            .OrderBy(s => s.Key)
            .Select(s => new TenantSettingDto(s.Key, s.Value, s.UpdatedAtUtc))
            .ToListAsync(ct);

    public Task<string?> GetValueAsync(string key, CancellationToken ct = default) =>
        _db.TenantSettings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => (string?)s.Value)
            .FirstOrDefaultAsync(ct);
}

internal sealed class SetupStatusQueries : ISetupStatusQueries
{
    private readonly SkyLisDbContext _db;
    public SetupStatusQueries(SkyLisDbContext db) => _db = db;

    public async Task<SetupStatusDto> StatusAsync(CancellationToken ct = default)
    {
        var branches = await _db.Branches.CountAsync(b => b.IsActive, ct);
        var departments = await _db.Branches.SelectMany(b => b.Departments).CountAsync(ct);
        var sampleTypes = await _db.SampleTypes.CountAsync(ct);
        var activeTests = await _db.LabTests.CountAsync(t => t.Status == Domain.Catalog.TestStatus.Active, ct);
        var panels = await _db.Panels.CountAsync(p => p.IsActive, ct);
        var users = await _db.Users.CountAsync(u => u.Status != Domain.Users.UserStatus.Deactivated, ct);
        var settings = await _db.TenantSettings.CountAsync(ct);

        return new SetupStatusDto(
            branches, departments, sampleTypes, activeTests, panels, users, settings,
            CatalogReady: sampleTypes > 0 && activeTests > 0,
            TeamReady: users > 1);
    }
}

internal sealed class PanelQueries : IPanelQueries
{
    private readonly SkyLisDbContext _db;
    public PanelQueries(SkyLisDbContext db) => _db = db;

    public async Task<IReadOnlyList<PanelDto>> ListAsync(CancellationToken ct = default)
    {
        var panels = await _db.Panels.AsNoTracking()
            .OrderBy(p => p.Code)
            .Select(p => new
            {
                p.Id, p.Code, p.Name, Price = p.Price.Amount, p.Price.Currency, p.IsActive,
                TestIds = p.Items.Select(i => i.TestId).ToList(),
            })
            .ToListAsync(ct);

        var allTestIds = panels.SelectMany(p => p.TestIds).Distinct().ToList();
        var tests = await _db.LabTests.AsNoTracking()
            .Where(t => allTestIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Code, t.Name })
            .ToDictionaryAsync(t => t.Id, ct);

        return panels.Select(p => new PanelDto(
            p.Id, p.Code, p.Name, p.Price, p.Currency, p.IsActive,
            p.TestIds.Select(id => tests.TryGetValue(id, out var t)
                ? new PanelMemberDto(id, t.Code, t.Name)
                : new PanelMemberDto(id, "?", "?")).ToList())).ToList();
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
