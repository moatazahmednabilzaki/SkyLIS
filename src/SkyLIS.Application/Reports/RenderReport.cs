using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Reports;
using SkyLIS.Domain.Visits;

namespace SkyLIS.Application.Reports;

public sealed record RenderedReportDto(
    Guid ReportId, string ReportNumber, int Version, string Kind, string ContentHash,
    DateTimeOffset RenderedAtUtc, string VerificationPath);

/// <summary>Everything the renderer needs for one report — assembled by the read side.</summary>
public sealed record ReportContent(
    string TenantLegalName, string PatientFullName, string PatientNumber, string Gender, int Age,
    string VisitNumber, DateTimeOffset VisitRegisteredAtUtc,
    IReadOnlyList<ReportResultLine> Results);

public sealed record ReportResultLine(
    string TestCode, decimal Value, string Unit, string Flag,
    decimal? RefLow, decimal? RefHigh, string? InterpretiveComment, string SignatureHash);

/// <summary>Rendering port: produces the immutable report artifact (HTML now; PDF converter later).</summary>
public interface IReportRenderer
{
    string RenderHtml(ReportContent content, ReportKind kind, string reportNumber, int version, DateTimeOffset nowUtc);
}

/// <summary>Read port for report assembly and worklists.</summary>
public interface IReportQueries
{
    Task<ReportContent?> GetContentAsync(Guid visitId, CancellationToken ct = default);
    Task<bool> HasOpenCriticalAsync(Guid visitId, CancellationToken ct = default);
    Task<int> CountForVisitAsync(Guid visitId, CancellationToken ct = default);
    Task<string?> ExistingReportNumberAsync(Guid visitId, CancellationToken ct = default);
    Task<bool> FinalExistsAsync(Guid visitId, CancellationToken ct = default);
    Task<IReadOnlyList<ReportWorklistRowDto>> WorklistAsync(CancellationToken ct = default);
}

public sealed record ReportWorklistRowDto(
    Guid VisitId, string VisitNumber, string PatientName, string VisitStatus,
    int MedicallyValidCount, int TotalTests,
    Guid? ReportId, string? ReportNumber, int? Version, string? Kind, string? ReportStatus,
    DateTimeOffset? RenderedAtUtc, int DeliveryCount);

/// <summary>
/// FR-REP-001 (P10.1): render a report for a visit. INTERIM releases the validated subset;
/// FINAL requires full validation, no open critical values, and fires the metering event.
/// </summary>
public sealed record RenderReportCommand(Guid VisitId, ReportKind Kind) : ICommand<RenderedReportDto>, IRequirePermission
{
    public string Permission => "reports.report.render";
}

internal sealed class RenderReportValidator : AbstractValidator<RenderReportCommand>
{
    public RenderReportValidator()
    {
        RuleFor(x => x.VisitId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum();
    }
}

internal sealed class RenderReportHandler : IRequestHandler<RenderReportCommand, RenderedReportDto>
{
    private readonly IVisitRepository _visits;
    private readonly ILabReportRepository _reports;
    private readonly IReportQueries _queries;
    private readonly IReportRenderer _renderer;
    private readonly INumberSeriesService _numbers;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public RenderReportHandler(
        IVisitRepository visits, ILabReportRepository reports, IReportQueries queries,
        IReportRenderer renderer, INumberSeriesService numbers, ITenantContext tenant, IClock clock)
    {
        _visits = visits;
        _reports = reports;
        _queries = queries;
        _renderer = renderer;
        _numbers = numbers;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<RenderedReportDto> Handle(RenderReportCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetAsync(request.VisitId, ct)
            ?? throw new NotFoundException("Visit", request.VisitId);
        if (request.Kind == ReportKind.Final && await _queries.FinalExistsAsync(visit.Id, ct))
            throw new ConflictException(
                "A FINAL report already exists for this visit; corrections go through the amendment flow (later slice).");

        var content = await _queries.GetContentAsync(visit.Id, ct)
            ?? throw new NotFoundException("ReportContent", request.VisitId);
        var hasOpenCritical = await _queries.HasOpenCriticalAsync(visit.Id, ct);
        var version = await _queries.CountForVisitAsync(visit.Id, ct) + 1;

        // The report number is per visit: the first render allocates it from the series,
        // every later version reuses it (versions share one report identity).
        var reportNumber = await _queries.ExistingReportNumberAsync(visit.Id, ct)
            ?? await _numbers.NextAsync("report", ct);
        var now = _clock.UtcNow;

        var html = _renderer.RenderHtml(content, request.Kind, reportNumber, version, now);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)));

        var fullyValidated = visit.Status == VisitStatus.Validated;
        var report = LabReport.Render(
            Guid.CreateVersion7(), _tenant.TenantId, visit.Id, visit.PatientId,
            reportNumber, version, request.Kind, html, hash,
            fullyValidated, hasOpenCritical, content.Results.Count, now);
        _reports.Add(report);
        _reports.AddVerification(ReportVerification.For(
            report.Id, content.TenantLegalName, content.PatientFullName,
            reportNumber, version, hash, now));

        if (request.Kind == ReportKind.Final)
            visit.MarkReported();

        return new RenderedReportDto(
            report.Id, report.ReportNumber, report.Version, report.Kind.ToString(),
            report.ContentHash, report.RenderedAtUtc,
            $"/api/v1/public/reports/{report.Id}/verify?hash={report.ContentHash}");
    }
}
