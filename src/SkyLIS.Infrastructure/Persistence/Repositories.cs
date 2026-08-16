using Microsoft.EntityFrameworkCore;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Billing;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Org;
using SkyLIS.Domain.Patients;
using SkyLIS.Domain.Platform;
using SkyLIS.Domain.Tenants;
using SkyLIS.Domain.Visits;

namespace SkyLIS.Infrastructure.Persistence;

// Per-aggregate repositories (write side). Tenant scoping is enforced by the global
// query filters + RLS; repositories never take a tenant id parameter.

internal sealed class TenantRepository : ITenantRepository
{
    private readonly SkyLisDbContext _db;
    public TenantRepository(SkyLisDbContext db) => _db = db;

    public Task<Tenant?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<bool> SubdomainExistsAsync(string subdomain, CancellationToken ct = default) =>
        _db.Tenants.AnyAsync(t => t.Subdomain == subdomain, ct);

    public async Task<IReadOnlyList<Guid>> GetPushTargetTenantIdsAsync(CancellationToken ct = default) =>
        await _db.Tenants
            .Where(t => t.Status == TenantStatus.Trial
                        || t.Status == TenantStatus.Active
                        || t.Status == TenantStatus.PastDue)
            .Select(t => t.Id)
            .ToListAsync(ct);

    public void Add(Tenant tenant) => _db.Tenants.Add(tenant);
}

internal sealed class PlanRepository : IPlanRepository
{
    private readonly SkyLisDbContext _db;
    public PlanRepository(SkyLisDbContext db) => _db = db;

    public Task<Plan?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        _db.Plans.FirstOrDefaultAsync(p => p.Code == code, ct);

    public void Add(Plan plan) => _db.Plans.Add(plan);
}

internal sealed class MasterTestRepository : IMasterTestRepository
{
    private readonly SkyLisDbContext _db;
    public MasterTestRepository(SkyLisDbContext db) => _db = db;

    public Task<MasterTest?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.MasterTests.FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) =>
        _db.MasterTests.AnyAsync(m => m.Code == code, ct);

    public void Add(MasterTest masterTest) => _db.MasterTests.Add(masterTest);
}

internal sealed class AttachmentRepository : IAttachmentRepository
{
    private readonly SkyLisDbContext _db;
    public AttachmentRepository(SkyLisDbContext db) => _db = db;

    public Task<Domain.Files.Attachment?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);

    public void Add(Domain.Files.Attachment attachment) => _db.Attachments.Add(attachment);
}

internal sealed class PatientRepository : IPatientRepository
{
    private readonly SkyLisDbContext _db;
    public PatientRepository(SkyLisDbContext db) => _db = db;

    public Task<Patient?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<bool> NationalIdExistsAsync(string nationalId, CancellationToken ct = default) =>
        _db.Patients.AnyAsync(p => p.NationalId == nationalId, ct);

    public void Add(Patient patient) => _db.Patients.Add(patient);
}

internal sealed class LabTestRepository : ILabTestRepository
{
    private readonly SkyLisDbContext _db;
    public LabTestRepository(SkyLisDbContext db) => _db = db;

    public Task<LabTest?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.LabTests.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<LabTest>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await _db.LabTests.Where(t => ids.Contains(t.Id)).ToListAsync(ct);

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) =>
        _db.LabTests.AnyAsync(t => t.Code == code, ct);

    public void Add(LabTest test) => _db.LabTests.Add(test);
}

internal sealed class SampleTypeRepository : ISampleTypeRepository
{
    private readonly SkyLisDbContext _db;
    public SampleTypeRepository(SkyLisDbContext db) => _db = db;

    public Task<SampleType?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.SampleTypes.Include(s => s.Conditions).FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<SampleType?> GetByNameAsync(string name, CancellationToken ct = default) =>
        _db.SampleTypes.Include(s => s.Conditions).FirstOrDefaultAsync(s => s.Name == name, ct);

    public async Task<IReadOnlyList<SampleCondition>> GetConditionsAsync(
        IReadOnlyCollection<Guid> conditionIds, CancellationToken ct = default) =>
        await _db.SampleTypes
            .SelectMany(s => s.Conditions)
            .Where(c => conditionIds.Contains(c.Id))
            .ToListAsync(ct);

    public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) =>
        _db.SampleTypes.AnyAsync(s => s.Name == name, ct);

    public void Add(SampleType sampleType) => _db.SampleTypes.Add(sampleType);
}

internal sealed class BranchRepository : IBranchRepository
{
    private readonly SkyLisDbContext _db;
    public BranchRepository(SkyLisDbContext db) => _db = db;

    public Task<Branch?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Branches.Include(b => b.Departments).FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) =>
        _db.Branches.AnyAsync(b => b.Code == code, ct);

    public Task<int> CountActiveAsync(CancellationToken ct = default) =>
        _db.Branches.CountAsync(b => b.IsActive, ct);

    public void Add(Branch branch) => _db.Branches.Add(branch);
}

internal sealed class CountryPackRepository : ICountryPackRepository
{
    private readonly SkyLisDbContext _db;
    public CountryPackRepository(SkyLisDbContext db) => _db = db;

    public Task<CountryPack?> GetByCountryAsync(string countryCode, CancellationToken ct = default) =>
        _db.CountryPacks.FirstOrDefaultAsync(p => p.CountryCode == countryCode, ct);

    public void Add(CountryPack pack) => _db.CountryPacks.Add(pack);
}

internal sealed class PanelRepository : IPanelRepository
{
    private readonly SkyLisDbContext _db;
    public PanelRepository(SkyLisDbContext db) => _db = db;

    public Task<Panel?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Panels.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Panel>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await _db.Panels.Include(p => p.Items).Where(p => ids.Contains(p.Id)).ToListAsync(ct);

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) =>
        _db.Panels.AnyAsync(p => p.Code == code, ct);

    public void Add(Panel panel) => _db.Panels.Add(panel);
}

internal sealed class VisitRepository : IVisitRepository
{
    private readonly SkyLisDbContext _db;
    public VisitRepository(SkyLisDbContext db) => _db = db;

    public Task<Visit?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Visits
            .Include(v => v.Tests)
            .Include(v => v.Samples)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

    public void Add(Visit visit) => _db.Visits.Add(visit);
}

internal sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly SkyLisDbContext _db;
    public InvoiceRepository(SkyLisDbContext db) => _db = db;

    public Task<Invoice?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Invoices.Include(i => i.Payments).FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<Invoice?> GetByVisitAsync(Guid visitId, CancellationToken ct = default) =>
        _db.Invoices.Include(i => i.Payments)
            .OrderBy(i => i.IssuedAtUtc)
            .FirstOrDefaultAsync(i => i.VisitId == visitId, ct);

    public async Task<IReadOnlyList<Invoice>> GetAllByVisitAsync(Guid visitId, CancellationToken ct = default) =>
        await _db.Invoices.Include(i => i.Payments)
            .Where(i => i.VisitId == visitId)
            .OrderBy(i => i.IssuedAtUtc)
            .ToListAsync(ct);

    public void Add(Invoice invoice) => _db.Invoices.Add(invoice);
}

internal sealed class CreditNoteRepository : ICreditNoteRepository
{
    private readonly SkyLisDbContext _db;
    public CreditNoteRepository(SkyLisDbContext db) => _db = db;

    public void Add(CreditNote creditNote) => _db.CreditNotes.Add(creditNote);
}

internal sealed class CashierShiftRepository : ICashierShiftRepository
{
    private readonly SkyLisDbContext _db;
    public CashierShiftRepository(SkyLisDbContext db) => _db = db;

    public Task<CashierShift?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.CashierShifts.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<CashierShift?> GetOpenByBranchAsync(Guid branchId, CancellationToken ct = default) =>
        _db.CashierShifts.FirstOrDefaultAsync(
            s => s.BranchId == branchId && s.Status == Domain.Billing.ShiftStatus.Open, ct);

    public void Add(CashierShift shift) => _db.CashierShifts.Add(shift);
}
