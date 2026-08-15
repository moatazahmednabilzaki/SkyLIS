using SkyLIS.Domain.Billing;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Org;
using SkyLIS.Domain.Patients;
using SkyLIS.Domain.Platform;
using SkyLIS.Domain.Reports;
using SkyLIS.Domain.Results;
using SkyLIS.Domain.Tenants;
using SkyLIS.Domain.Visits;

namespace SkyLIS.Application.Common;

/// <summary>Authenticated caller: identity, tenant scope, and granted permission codes.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
    bool IsPlatformOperator { get; }
    bool HasPermission(string permission);
}

/// <summary>
/// The ambient tenant for this request, resolved ONLY from trusted sources
/// (identity claims / verified host mapping) — never from a request body.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    bool HasTenant { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Transport-level caller context for the audit trail (where): client IP/device.</summary>
public interface IClientContext
{
    string? IpAddress { get; }
}

/// <summary>Commit point for the aggregate transaction; called once per command by UnitOfWorkBehavior.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Tenant-scoped, gap-tolerant human-facing number series (visit, patient, invoice numbers).
/// Pass a branch code as <paramref name="scope"/> to run the series per branch (P03.2).
/// </summary>
public interface INumberSeriesService
{
    Task<string> NextAsync(string seriesKind, string? scope = null, CancellationToken ct = default);
}

// ---- Per-aggregate repositories (write side). No generic repository by design (EAA rule). ----

public interface ITenantRepository
{
    Task<Tenant?> GetAsync(Guid id, CancellationToken ct = default);
    Task<bool> SubdomainExistsAsync(string subdomain, CancellationToken ct = default);
    void Add(Tenant tenant);
}

public interface IPatientRepository
{
    Task<Patient?> GetAsync(Guid id, CancellationToken ct = default);
    Task<bool> NationalIdExistsAsync(string nationalId, CancellationToken ct = default);
    void Add(Patient patient);
}

public interface ILabTestRepository
{
    Task<LabTest?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<LabTest>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);
    void Add(LabTest test);
}

public interface ISampleTypeRepository
{
    Task<SampleType?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SampleCondition>> GetConditionsAsync(IReadOnlyCollection<Guid> conditionIds, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, CancellationToken ct = default);
    void Add(SampleType sampleType);
}

public interface IBranchRepository
{
    Task<Branch?> GetAsync(Guid id, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);
    void Add(Branch branch);
}

/// <summary>Country packs are platform-scoped (no tenant filter): read during tenant seeding.</summary>
public interface ICountryPackRepository
{
    Task<CountryPack?> GetByCountryAsync(string countryCode, CancellationToken ct = default);
    void Add(CountryPack pack);
}

public interface IVisitRepository
{
    Task<Visit?> GetAsync(Guid id, CancellationToken ct = default);
    void Add(Visit visit);
}

public interface ITestResultRepository
{
    Task<TestResult?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>The non-voided result for a visit test line, if any (reruns void results).</summary>
    Task<TestResult?> GetActiveByLineAsync(Guid visitTestId, CancellationToken ct = default);
    void Add(TestResult result);
}

public interface ILabReportRepository
{
    Task<LabReport?> GetAsync(Guid id, CancellationToken ct = default);
    void Add(LabReport report);
    /// <summary>Verification records are platform-scoped (public QR check) — no tenant filter.</summary>
    void AddVerification(ReportVerification verification);
}

public interface IInvoiceRepository
{
    Task<Invoice?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Invoice?> GetByVisitAsync(Guid visitId, CancellationToken ct = default);
    void Add(Invoice invoice);
}

public interface ICreditNoteRepository
{
    void Add(CreditNote creditNote);
}

public interface ICashierShiftRepository
{
    Task<CashierShift?> GetAsync(Guid id, CancellationToken ct = default);
    Task<CashierShift?> GetOpenByBranchAsync(Guid branchId, CancellationToken ct = default);
    void Add(CashierShift shift);
}
