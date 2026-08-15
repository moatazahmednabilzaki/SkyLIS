using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Reports;

/// <summary>
/// P10.2 public verification result: confirms issuer, patient initials, issue time, and
/// hash validity WITHOUT exposing clinical content. Served anonymously (QR landing).
/// </summary>
public sealed record VerificationResultDto(
    bool Found, bool HashValid, string? IssuerName, string? PatientInitials,
    string? ReportNumber, int? Version, DateTimeOffset? IssuedAtUtc);

/// <summary>Anonymous by design — carries no IRequirePermission and no tenant scope.</summary>
public sealed record VerifyReportQuery(Guid ReportId, string Hash) : IQuery<VerificationResultDto>;

public interface IReportVerificationQueries
{
    Task<VerificationResultDto> VerifyAsync(Guid reportId, string hash, CancellationToken ct = default);
}

internal sealed class VerifyReportHandler : IRequestHandler<VerifyReportQuery, VerificationResultDto>
{
    private readonly IReportVerificationQueries _queries;

    public VerifyReportHandler(IReportVerificationQueries queries) => _queries = queries;

    public Task<VerificationResultDto> Handle(VerifyReportQuery request, CancellationToken ct) =>
        _queries.VerifyAsync(request.ReportId, request.Hash, ct);
}
