using SkyLIS.Application.Common;
using SkyLIS.Domain.Billing;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Org;
using SkyLIS.Domain.Patients;
using SkyLIS.Domain.Visits;

namespace SkyLIS.Application.Tests;

// Hand-rolled in-memory fakes — no mocking framework needed for these ports.

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
}

internal sealed class FakeTenantContext : ITenantContext
{
    public Guid TenantId { get; set; } = Guid.NewGuid();
    public bool HasTenant => true;
}

internal sealed class FakeNumberSeries : INumberSeriesService
{
    private readonly Dictionary<string, int> _counters = [];

    public Task<string> NextAsync(string seriesKind, string? scope = null, CancellationToken ct = default)
    {
        var kind = scope is null ? seriesKind : $"{seriesKind}:{scope}";
        _counters[kind] = _counters.GetValueOrDefault(kind) + 1;
        var prefix = seriesKind switch { "visit" => "V", "patient" => "PN", "invoice" => "INV", _ => "X" };
        return Task.FromResult(scope is null
            ? $"{prefix}-260815-{_counters[kind]:D4}"
            : $"{prefix}-{scope}-260815-{_counters[kind]:D4}");
    }
}

internal sealed class FakeBranchRepository : IBranchRepository
{
    public List<Branch> Items { get; } = [];

    public Task<Branch?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(b => b.Id == id));

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(Items.Any(b => b.Code == code));

    public void Add(Branch branch) => Items.Add(branch);
}

internal sealed class FakePatientRepository : IPatientRepository
{
    public List<Patient> Items { get; } = [];

    public Task<Patient?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

    public Task<bool> NationalIdExistsAsync(string nationalId, CancellationToken ct = default) =>
        Task.FromResult(Items.Any(p => p.NationalId == nationalId));

    public void Add(Patient patient) => Items.Add(patient);
}

internal sealed class FakeLabTestRepository : ILabTestRepository
{
    public List<LabTest> Items { get; } = [];

    public Task<LabTest?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(t => t.Id == id));

    public Task<IReadOnlyList<LabTest>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LabTest>>(Items.Where(t => ids.Contains(t.Id)).ToList());

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(Items.Any(t => t.Code == code));

    public void Add(LabTest test) => Items.Add(test);
}

internal sealed class FakeSampleTypeRepository : ISampleTypeRepository
{
    public List<SampleType> Items { get; } = [];

    public Task<SampleType?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(s => s.Id == id));

    public Task<IReadOnlyList<SampleCondition>> GetConditionsAsync(
        IReadOnlyCollection<Guid> conditionIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SampleCondition>>(
            Items.SelectMany(s => s.Conditions).Where(c => conditionIds.Contains(c.Id)).ToList());

    public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(Items.Any(s => s.Name == name));

    public void Add(SampleType sampleType) => Items.Add(sampleType);
}

internal sealed class FakeVisitRepository : IVisitRepository
{
    public List<Visit> Items { get; } = [];

    public Task<Visit?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(v => v.Id == id));

    public void Add(Visit visit) => Items.Add(visit);
}

internal sealed class FakeInvoiceRepository : IInvoiceRepository
{
    public List<Invoice> Items { get; } = [];

    public Task<Invoice?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(i => i.Id == id));

    public Task<Invoice?> GetByVisitAsync(Guid visitId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(i => i.VisitId == visitId));

    public void Add(Invoice invoice) => Items.Add(invoice);
}
