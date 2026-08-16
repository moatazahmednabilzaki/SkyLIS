using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Reports;

public enum ReportKind { Interim = 0, Final = 1, Amended = 2 }
public enum ReportStatus { Rendered = 0, Delivered = 1 }
public enum DeliveryOutcome { Sent = 0, Failed = 1 }

/// <summary>
/// Tenant-owned aggregate: one rendered report version for a visit (M10).
/// Rendered artifacts are immutable — the content hash is computed at issue and never
/// changes; a material change requires a new version (amendment flow, later slice).
/// A FINAL report cannot be rendered while an open critical value exists (P09.4) or
/// while results are still outstanding; the first FINAL render is the metering event
/// (one finalized report per visit — FR-SYS-011).
/// </summary>
public sealed class LabReport : AggregateRoot, ITenantOwned
{
    private readonly List<ReportDelivery> _deliveries = [];

    public Guid TenantId { get; private set; }
    public Guid VisitId { get; private set; }
    public Guid PatientId { get; private set; }
    public string ReportNumber { get; private set; } = null!;
    public int Version { get; private set; }
    public ReportKind Kind { get; private set; }
    public ReportStatus Status { get; private set; }
    public string ContentHash { get; private set; } = null!;
    public string ContentHtml { get; private set; } = null!;
    public DateTimeOffset RenderedAtUtc { get; private set; }

    public IReadOnlyCollection<ReportDelivery> Deliveries => _deliveries.AsReadOnly();

    private LabReport() { } // EF

    public static LabReport Render(
        Guid id, Guid tenantId, Guid visitId, Guid patientId, string reportNumber, int version,
        ReportKind kind, string contentHtml, string contentHash,
        bool visitFullyValidated, bool hasOpenCriticalValue, int medicallyValidResultCount,
        DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        if (string.IsNullOrWhiteSpace(reportNumber)) throw new DomainException("Report number is required.");
        if (version < 1) throw new DomainException("Report version starts at 1.");
        if (string.IsNullOrWhiteSpace(contentHtml) || string.IsNullOrWhiteSpace(contentHash))
            throw new DomainException("Rendered content and its hash are required.");
        if (medicallyValidResultCount == 0)
            throw new DomainException("A report requires at least one medically valid result.");
        if (kind is ReportKind.Final or ReportKind.Amended && hasOpenCriticalValue)
            throw new DomainException(
                "A report containing an undocumented open critical value cannot reach Final (P09.4).");
        if (kind is ReportKind.Final or ReportKind.Amended && !visitFullyValidated)
            throw new DomainException(
                "A FINAL report requires every test on the visit to be medically valid; render an INTERIM report instead.");

        var report = new LabReport
        {
            Id = id,
            TenantId = tenantId,
            VisitId = visitId,
            PatientId = patientId,
            ReportNumber = reportNumber,
            Version = version,
            Kind = kind,
            Status = ReportStatus.Rendered,
            ContentHtml = contentHtml,
            ContentHash = contentHash,
            RenderedAtUtc = nowUtc,
        };
        report.Raise(new ReportRendered(id, tenantId, visitId, reportNumber, version, kind.ToString()));
        if (kind == ReportKind.Final)
            report.Raise(new ReportFinalized(id, tenantId, visitId)); // the metering unit (FR-SYS-011)
        return report;
    }

    /// <summary>Records one delivery attempt; the report becomes Delivered on the first success.</summary>
    public void RecordDelivery(Guid deliveryId, string channel, string destination, DeliveryOutcome outcome, DateTimeOffset nowUtc)
    {
        var delivery = new ReportDelivery(deliveryId, TenantId, Id, channel, destination, outcome, nowUtc);
        _deliveries.Add(delivery);
        if (outcome == DeliveryOutcome.Sent && Status == ReportStatus.Rendered)
        {
            Status = ReportStatus.Delivered;
            Raise(new ReportDelivered(Id, TenantId, VisitId, channel));
        }
    }
}

/// <summary>One delivery attempt: channel, destination, outcome — the evidentiary log (P10.1).</summary>
public sealed class ReportDelivery : Entity, ITenantOwned
{
    private static readonly string[] Channels = ["print", "email", "whatsapp", "portal"];

    public Guid TenantId { get; private set; }
    public Guid ReportId { get; private set; }
    public string Channel { get; private set; } = null!;
    public string Destination { get; private set; } = null!;
    public DeliveryOutcome Outcome { get; private set; }
    public DateTimeOffset AttemptedAtUtc { get; private set; }

    private ReportDelivery() { } // EF

    internal ReportDelivery(Guid id, Guid tenantId, Guid reportId, string channel, string destination,
        DeliveryOutcome outcome, DateTimeOffset attemptedAtUtc) : base(id)
    {
        if (!Channels.Contains(channel)) throw new DomainException($"Unknown delivery channel '{channel}'.");
        if (string.IsNullOrWhiteSpace(destination)) throw new DomainException("Delivery destination is required.");
        TenantId = tenantId;
        ReportId = reportId;
        Channel = channel;
        Destination = destination.Trim();
        Outcome = outcome;
        AttemptedAtUtc = attemptedAtUtc;
    }
}

/// <summary>
/// Platform-scoped (NOT tenant-owned) verification record backing the public QR check
/// (P10.2). Holds no clinical content — issuer, patient initials, timestamp, hash only —
/// so the anonymous endpoint can validate authenticity across tenants without touching
/// RLS-protected data.
/// </summary>
public sealed class ReportVerification : Entity
{
    public string IssuerName { get; private set; } = null!;
    public string PatientInitials { get; private set; } = null!;
    public string ReportNumber { get; private set; } = null!;
    public int Version { get; private set; }
    public string ContentHash { get; private set; } = null!;
    public DateTimeOffset IssuedAtUtc { get; private set; }

    private ReportVerification() { } // EF

    public static ReportVerification For(
        Guid reportId, string issuerName, string patientFullName, string reportNumber,
        int version, string contentHash, DateTimeOffset issuedAtUtc)
    {
        var initials = string.Join(".", patientFullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => char.ToUpperInvariant(part[0]))) + ".";
        return new ReportVerification
        {
            Id = reportId,
            IssuerName = issuerName,
            PatientInitials = initials,
            ReportNumber = reportNumber,
            Version = version,
            ContentHash = contentHash,
            IssuedAtUtc = issuedAtUtc,
        };
    }
}

public sealed record ReportRendered(Guid ReportId, Guid TenantId, Guid VisitId, string ReportNumber, int Version, string Kind) : DomainEvent, ITenantEvent;
public sealed record ReportFinalized(Guid ReportId, Guid TenantId, Guid VisitId) : DomainEvent, ITenantEvent;
public sealed record ReportDelivered(Guid ReportId, Guid TenantId, Guid VisitId, string Channel) : DomainEvent, ITenantEvent;
