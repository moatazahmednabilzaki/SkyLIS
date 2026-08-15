using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Patients;

/// <summary>
/// The identity-confirmation card (FR-PAT-001): last visit date, age, and gender let the
/// receptionist confirm "same patient" before reusing the record.
/// </summary>
public sealed record PatientSearchResultDto(
    Guid Id,
    string PatientNumber,
    string FullName,
    string MobileMasked,
    string Gender,
    int Age,
    DateTimeOffset? LastVisitAtUtc);

/// <summary>FR-PAT-001: search by mobile number, part of name, national ID, or patient number.</summary>
public sealed record SearchPatientsQuery(string Term) : IQuery<IReadOnlyList<PatientSearchResultDto>>, IRequirePermission
{
    public string Permission => "patients.patient.read";
}

public interface IPatientQueries
{
    Task<IReadOnlyList<PatientSearchResultDto>> SearchAsync(string term, DateOnly today, CancellationToken ct = default);
}

internal sealed class SearchPatientsHandler : IRequestHandler<SearchPatientsQuery, IReadOnlyList<PatientSearchResultDto>>
{
    private readonly IPatientQueries _queries;
    private readonly IClock _clock;

    public SearchPatientsHandler(IPatientQueries queries, IClock clock)
    {
        _queries = queries;
        _clock = clock;
    }

    public Task<IReadOnlyList<PatientSearchResultDto>> Handle(SearchPatientsQuery request, CancellationToken ct) =>
        _queries.SearchAsync(request.Term.Trim(), DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime), ct);
}
