using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Patients;

public sealed record Patient360VisitDto(
    Guid VisitId, string VisitNumber, string BranchCode, DateTimeOffset RegisteredAtUtc,
    string Status, bool IsStat, Guid InvoiceId, string InvoiceStatus,
    decimal Total, decimal Balance, string Currency);

public sealed record Patient360ReportDto(
    Guid ReportId, string ReportNumber, int Version, string Kind, string Status, DateTimeOffset RenderedAtUtc);

public sealed record Patient360Dto(
    Guid Id, string PatientNumber, string FullName, string Gender, DateOnly DateOfBirth, int Age,
    string Mobile, string? NationalId, DateTimeOffset RegisteredAtUtc, DateTimeOffset? LastVisitAtUtc,
    decimal OutstandingBalance, string Currency,
    IReadOnlyList<Patient360VisitDto> Visits,
    IReadOnlyList<Patient360ReportDto> Reports,
    IReadOnlyList<string> TestCodes);

public interface IPatient360Queries
{
    Task<Patient360Dto?> GetAsync(Guid patientId, DateOnly today, CancellationToken ct = default);
}

/// <summary>
/// P04.3 Patient 360: the complete story of one patient — demographics, visit history,
/// financial standing, and rendered reports — the launchpad for the cumulative view (P10.3).
/// </summary>
public sealed record GetPatient360Query(Guid PatientId) : IQuery<Patient360Dto>, IRequirePermission
{
    public string Permission => "patients.patient.read";
}

internal sealed class GetPatient360Handler : IRequestHandler<GetPatient360Query, Patient360Dto>
{
    private readonly IPatient360Queries _queries;
    private readonly IClock _clock;

    public GetPatient360Handler(IPatient360Queries queries, IClock clock)
    {
        _queries = queries;
        _clock = clock;
    }

    public async Task<Patient360Dto> Handle(GetPatient360Query request, CancellationToken ct) =>
        await _queries.GetAsync(request.PatientId, DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime), ct)
            ?? throw new NotFoundException("Patient", request.PatientId);
}
