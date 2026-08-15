using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using SkyLIS.Application.Common;
using SkyLIS.Application.Reports;
using SkyLIS.Domain.Reports;
using SkyLIS.Domain.Results;
using SkyLIS.Domain.Visits;
using SkyLIS.Infrastructure.Persistence;

namespace SkyLIS.Infrastructure.Reports;

internal sealed class LabReportConfig : IEntityTypeConfiguration<LabReport>
{
    public void Configure(EntityTypeBuilder<LabReport> b)
    {
        b.ToTable("lab_reports", "reports");
        b.HasKey(r => r.Id);
        b.Property(r => r.TenantId).IsRequired();
        b.HasIndex(r => new { r.TenantId, r.VisitId });
        b.HasIndex(r => new { r.TenantId, r.ReportNumber, r.Version }).IsUnique();
        b.Property(r => r.ReportNumber).HasMaxLength(30).IsRequired();
        b.Property(r => r.Kind).HasConversion<string>().HasMaxLength(10);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.ContentHash).HasMaxLength(64).IsRequired();
        b.Property(r => r.ContentHtml).IsRequired();
        b.HasMany(r => r.Deliveries).WithOne().HasForeignKey(d => d.ReportId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r => r.Deliveries).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class ReportDeliveryConfig : IEntityTypeConfiguration<ReportDelivery>
{
    public void Configure(EntityTypeBuilder<ReportDelivery> b)
    {
        b.ToTable("report_deliveries", "reports");
        b.HasKey(d => d.Id);
        b.Property(d => d.TenantId).IsRequired();
        b.Property(d => d.Channel).HasMaxLength(20).IsRequired();
        b.Property(d => d.Destination).HasMaxLength(200).IsRequired();
        b.Property(d => d.Outcome).HasConversion<string>().HasMaxLength(10);
    }
}

internal sealed class ReportVerificationConfig : IEntityTypeConfiguration<ReportVerification>
{
    public void Configure(EntityTypeBuilder<ReportVerification> b)
    {
        // Platform schema on purpose: readable by the anonymous verification endpoint;
        // holds no clinical content (issuer, initials, number, hash, timestamp only).
        b.ToTable("report_verifications", "platform");
        b.HasKey(v => v.Id);
        b.Property(v => v.IssuerName).HasMaxLength(200).IsRequired();
        b.Property(v => v.PatientInitials).HasMaxLength(20).IsRequired();
        b.Property(v => v.ReportNumber).HasMaxLength(30).IsRequired();
        b.Property(v => v.ContentHash).HasMaxLength(64).IsRequired();
    }
}

internal sealed class LabReportRepository : ILabReportRepository
{
    private readonly SkyLisDbContext _db;
    public LabReportRepository(SkyLisDbContext db) => _db = db;

    public Task<LabReport?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.LabReports.Include(r => r.Deliveries).FirstOrDefaultAsync(r => r.Id == id, ct);

    public void Add(LabReport report) => _db.LabReports.Add(report);

    public void AddVerification(ReportVerification verification) => _db.ReportVerifications.Add(verification);
}

internal sealed class ReportQueries : IReportQueries
{
    private readonly SkyLisDbContext _db;
    public ReportQueries(SkyLisDbContext db) => _db = db;

    public async Task<ReportContent?> GetContentAsync(Guid visitId, CancellationToken ct = default)
    {
        var visit = await _db.Visits.AsNoTracking()
            .Where(v => v.Id == visitId)
            .Select(v => new { v.Id, v.VisitNumber, v.RegisteredAtUtc, v.PatientId, v.TenantId })
            .FirstOrDefaultAsync(ct);
        if (visit is null) return null;

        var patient = await _db.Patients.AsNoTracking()
            .Where(p => p.Id == visit.PatientId)
            .Select(p => new { p.FullName, p.PatientNumber, p.Sex, p.DateOfBirth })
            .FirstAsync(ct);

        var tenantName = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == visit.TenantId)
            .Select(t => t.LegalName)
            .FirstOrDefaultAsync(ct) ?? "Sky LIS Laboratory";

        var results = await _db.TestResults.AsNoTracking()
            .Where(r => r.VisitId == visitId && r.Status == ResultStatus.MedicallyValid)
            .OrderBy(r => r.TestCode)
            .Select(r => new { r.TestCode, r.Value, r.Unit, r.Flag, r.InterpretiveComment, r.SignatureHash, r.VisitTestId })
            .ToListAsync(ct);

        var testIds = await _db.Visits.AsNoTracking()
            .Where(v => v.Id == visitId)
            .SelectMany(v => v.Tests)
            .Select(t => new { t.Id, t.TestId })
            .ToListAsync(ct);
        var schemaByLine = new Dictionary<Guid, (decimal? Low, decimal? High)>();
        var catalogIds = testIds.Select(t => t.TestId).Distinct().ToList();
        var schemas = await _db.LabTests.AsNoTracking()
            .Where(t => catalogIds.Contains(t.Id) && t.ResultSchema != null)
            .Select(t => new { t.Id, t.ResultSchema!.RefLow, t.ResultSchema.RefHigh })
            .ToDictionaryAsync(t => t.Id, ct);
        foreach (var line in testIds)
        {
            if (schemas.TryGetValue(line.TestId, out var s))
                schemaByLine[line.Id] = (s.RefLow, s.RefHigh);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - patient.DateOfBirth.Year;
        if (today < patient.DateOfBirth.AddYears(age)) age--;

        return new ReportContent(
            tenantName, patient.FullName, patient.PatientNumber, patient.Sex.ToString(), age,
            visit.VisitNumber, visit.RegisteredAtUtc,
            results.Select(r =>
            {
                var range = schemaByLine.GetValueOrDefault(r.VisitTestId);
                return new ReportResultLine(
                    r.TestCode, r.Value, r.Unit, r.Flag.ToString(),
                    range.Low, range.High, r.InterpretiveComment, r.SignatureHash ?? "");
            }).ToList());
    }

    public Task<bool> HasOpenCriticalAsync(Guid visitId, CancellationToken ct = default) =>
        _db.TestResults.AsNoTracking()
            .AnyAsync(r => r.VisitId == visitId
                           && r.Critical != null && r.Critical.State != CriticalState.Closed
                           && r.Status != ResultStatus.RerunOrdered, ct);

    public Task<int> CountForVisitAsync(Guid visitId, CancellationToken ct = default) =>
        _db.LabReports.AsNoTracking().CountAsync(r => r.VisitId == visitId, ct);

    public Task<string?> ExistingReportNumberAsync(Guid visitId, CancellationToken ct = default) =>
        _db.LabReports.AsNoTracking()
            .Where(r => r.VisitId == visitId)
            .Select(r => (string?)r.ReportNumber)
            .FirstOrDefaultAsync(ct);

    public Task<bool> FinalExistsAsync(Guid visitId, CancellationToken ct = default) =>
        _db.LabReports.AsNoTracking().AnyAsync(r => r.VisitId == visitId && r.Kind == ReportKind.Final, ct);

    public async Task<IReadOnlyList<ReportWorklistRowDto>> WorklistAsync(CancellationToken ct = default)
    {
        // Visits with at least one medically valid result: candidates and rendered reports.
        var candidates = await _db.Visits.AsNoTracking()
            .Where(v => v.Status == VisitStatus.Validated || v.Status == VisitStatus.InProcess
                        || v.Status == VisitStatus.Reported)
            .OrderByDescending(v => v.RegisteredAtUtc)
            .Take(100)
            .Select(v => new
            {
                v.Id, v.VisitNumber, v.Status, v.PatientId,
                Total = v.Tests.Count(t => t.Status != VisitTestStatus.Cancelled),
                MedValid = v.Tests.Count(t => t.Status == VisitTestStatus.MedValid || t.Status == VisitTestStatus.Reported),
            })
            .Where(v => v.MedValid > 0 || v.Status == VisitStatus.Reported)
            .ToListAsync(ct);

        var visitIds = candidates.Select(c => c.Id).ToList();
        var reports = await _db.LabReports.AsNoTracking()
            .Where(r => visitIds.Contains(r.VisitId))
            .Select(r => new
            {
                r.VisitId, r.Id, r.ReportNumber, r.Version, r.Kind, r.Status, r.RenderedAtUtc,
                DeliveryCount = r.Deliveries.Count,
            })
            .ToListAsync(ct);
        var latestByVisit = reports
            .GroupBy(r => r.VisitId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Version).First());

        var patientIds = candidates.Select(c => c.PatientId).Distinct().ToList();
        var names = await _db.Patients.AsNoTracking()
            .Where(p => patientIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName })
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct);

        return candidates.Select(c =>
        {
            var report = latestByVisit.GetValueOrDefault(c.Id);
            return new ReportWorklistRowDto(
                c.Id, c.VisitNumber, names.GetValueOrDefault(c.PatientId, "(unknown)"),
                c.Status.ToString(), c.MedValid, c.Total,
                report?.Id, report?.ReportNumber, report?.Version, report?.Kind.ToString(),
                report?.Status.ToString(), report?.RenderedAtUtc, report?.DeliveryCount ?? 0);
        }).ToList();
    }
}

internal sealed class ReportVerificationQueries : IReportVerificationQueries
{
    private readonly SkyLisDbContext _db;
    public ReportVerificationQueries(SkyLisDbContext db) => _db = db;

    public async Task<VerificationResultDto> VerifyAsync(Guid reportId, string hash, CancellationToken ct = default)
    {
        var record = await _db.ReportVerifications.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == reportId, ct);
        if (record is null)
            return new VerificationResultDto(false, false, null, null, null, null, null);

        var valid = string.Equals(record.ContentHash, hash, StringComparison.OrdinalIgnoreCase);
        return new VerificationResultDto(
            true, valid, record.IssuerName, record.PatientInitials,
            record.ReportNumber, record.Version, record.IssuedAtUtc);
    }
}

/// <summary>Development sender: logs the outbound message and reports success.</summary>
internal sealed class DevNotificationSender : INotificationSender
{
    private readonly ILogger<DevNotificationSender> _logger;
    public DevNotificationSender(ILogger<DevNotificationSender> logger) => _logger = logger;

    public Task<bool> SendAsync(string channel, string destination, string subject, CancellationToken ct = default)
    {
        _logger.LogInformation("DEV DELIVERY via {Channel} to {Destination}: {Subject}", channel, destination, subject);
        return Task.FromResult(true);
    }
}
