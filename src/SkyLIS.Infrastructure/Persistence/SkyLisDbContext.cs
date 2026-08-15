using Microsoft.EntityFrameworkCore;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Billing;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Patients;
using SkyLIS.Domain.Reports;
using SkyLIS.Domain.Results;
using SkyLIS.Domain.Tenants;
using SkyLIS.Domain.Visits;
using SkyLIS.Infrastructure.Outbox;
using SkyLIS.Infrastructure.Tenancy;

namespace SkyLIS.Infrastructure.Persistence;

/// <summary>
/// One DbContext, schema per module. Tenant isolation: RLS in PostgreSQL (see
/// Scripts/enable-rls.sql) plus EF global query filters as defense in depth.
/// Implements IUnitOfWork directly — no redundant unit-of-work abstraction (EAA rule).
/// </summary>
public sealed class SkyLisDbContext : DbContext, IUnitOfWork
{
    private readonly TenantContext _tenantContext;

    public SkyLisDbContext(DbContextOptions<SkyLisDbContext> options, TenantContext tenantContext)
        : base(options) => _tenantContext = tenantContext;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<LabTest> LabTests => Set<LabTest>();
    public DbSet<SampleType> SampleTypes => Set<SampleType>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<TestResult> TestResults => Set<TestResult>();
    public DbSet<LabReport> LabReports => Set<LabReport>();
    public DbSet<ReportVerification> ReportVerifications => Set<ReportVerification>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SkyLisDbContext).Assembly);

        // Ids are always supplied by the domain (UUID v7). Without this, EF treats
        // graph-discovered children with set keys as Modified and issues phantom UPDATEs
        // (e.g., the recollection sample spawned inside Visit.RejectSample).
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var primaryKey = entityType.FindPrimaryKey();
            if (primaryKey is null) continue;
            foreach (var property in primaryKey.Properties)
                property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }

        // Defense-in-depth tenant filter on every tenant-owned aggregate root.
        modelBuilder.Entity<Patient>().HasQueryFilter(p => p.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<LabTest>().HasQueryFilter(t => t.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<SampleType>().HasQueryFilter(s => s.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Visit>().HasQueryFilter(v => v.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<TestResult>().HasQueryFilter(r => r.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<LabReport>().HasQueryFilter(r => r.TenantId == _tenantContext.TenantId);
        // ReportVerification intentionally has NO tenant filter: platform-scoped, PHI-free,
        // read by the anonymous QR verification endpoint.
        modelBuilder.Entity<Invoice>().HasQueryFilter(i => i.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<NumberSeries>().HasQueryFilter(n => n.TenantId == _tenantContext.TenantId);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        CollectDomainEventsIntoOutbox();
        return await base.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Outbox pattern: domain events are persisted in the SAME transaction as the state
    /// change; a background dispatcher publishes them reliably afterwards.
    /// </summary>
    private void CollectDomainEventsIntoOutbox()
    {
        var aggregates = ChangeTracker.Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
                OutboxMessages.Add(OutboxMessage.From(domainEvent, _tenantContext.HasTenant ? _tenantContext.TenantId : null));
            aggregate.ClearDomainEvents();
        }
    }
}

/// <summary>Tenant-scoped named counter backing INumberSeriesService. Concurrency: xmin (shadow).</summary>
public sealed class NumberSeries
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Kind { get; set; } = null!;
    public long LastValue { get; set; }
}
