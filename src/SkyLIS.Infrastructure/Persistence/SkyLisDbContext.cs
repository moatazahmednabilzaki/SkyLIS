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
using SkyLIS.Infrastructure.Audit;
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
    private readonly ICurrentUser? _currentUser;
    private readonly IClientContext? _clientContext;

    public SkyLisDbContext(
        DbContextOptions<SkyLisDbContext> options, TenantContext tenantContext,
        ICurrentUser? currentUser = null, IClientContext? clientContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clientContext = clientContext;
    }

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
    public DbSet<InboxConsumption> InboxConsumptions => Set<InboxConsumption>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
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

        // FR-SYS-001 / NFR-007: audit events are computed from the change tracker and
        // written IN the same transaction as the business change.
        var tenantId = _tenantContext.HasTenant ? _tenantContext.TenantId : (Guid?)null;
        var auditEvents = AuditCollector.Collect(
            ChangeTracker, tenantId, _currentUser?.UserId, _clientContext?.IpAddress, DateTimeOffset.UtcNow);
        if (auditEvents.Count == 0)
            return await base.SaveChangesAsync(ct);

        // Hash chain per tenant: serialize appends with a per-tenant advisory lock so the
        // previous-hash read cannot fork under concurrency; the lock releases on commit.
        await using var transaction = Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(ct)
            : null;

        foreach (var group in auditEvents.GroupBy(e => e.TenantId))
        {
            var chainKey = ChainLockKey(group.Key);
            await Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({chainKey})", ct);
            var previousHash = await Database
                .SqlQuery<string>($@"
                    SELECT hash AS ""Value"" FROM audit.audit_events
                    WHERE tenant_id IS NOT DISTINCT FROM {group.Key}
                    ORDER BY occurred_at_utc DESC, id DESC LIMIT 1")
                .FirstOrDefaultAsync(ct) ?? AuditEvent.GenesisHash;

            foreach (var auditEvent in group.OrderBy(e => e.Id))
            {
                auditEvent.PreviousHash = previousHash;
                auditEvent.Hash = auditEvent.ComputeHash(previousHash);
                previousHash = auditEvent.Hash;
            }
        }
        AuditEvents.AddRange(auditEvents);

        var result = await base.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return result;
    }

    private static long ChainLockKey(Guid? tenantId)
    {
        var bytes = (tenantId ?? Guid.Empty).ToByteArray();
        return BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8);
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
