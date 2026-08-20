using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Results;

public sealed record PendingEntryDto(
    Guid VisitId, string VisitNumber, Guid VisitTestId, string TestCode, string PatientName,
    string SampleBarcode, bool IsStat, string? Unit, decimal? RefLow, decimal? RefHigh, decimal? PreviousValue);

public sealed record ResultQueueItemDto(
    Guid ResultId, Guid VisitId, string VisitNumber, string PatientName, string TestCode,
    decimal Value, string Unit, string Flag, bool DeltaFlagged, decimal? PreviousValue,
    string Status, DateTimeOffset EnteredAtUtc);

public sealed record CriticalQueueItemDto(
    Guid ResultId, string VisitNumber, string PatientName, string TestCode,
    decimal Value, string Unit, string Flag, string CriticalState,
    DateTimeOffset FlaggedAtUtc, string? CalledPerson, bool ReadBackConfirmed);

public sealed record CumulativePointDto(
    Guid ResultId, string VisitNumber, decimal Value, string Unit, string Flag, bool IsAmended,
    DateTimeOffset ValidatedAtUtc, decimal? RefLow, decimal? RefHigh);

public sealed record VisitResultDto(
    Guid ResultId, Guid VisitTestId, string TestCode, decimal Value, string Unit, string Flag,
    string Status, bool IsAmended, decimal? ValueBeforeAmendment, string? AmendmentReason);

/// <summary>Read ports for the M09 worklists — DTO projections only.</summary>
public interface IResultQueries
{
    Task<decimal?> GetPreviousValueAsync(Guid patientId, Guid testId, CancellationToken ct = default);
    Task<IReadOnlyList<PendingEntryDto>> PendingEntryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ResultQueueItemDto>> TechnicalQueueAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ResultQueueItemDto>> MedicalQueueAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CriticalQueueItemDto>> CriticalQueueAsync(CancellationToken ct = default);
    /// <summary>P10.3: the patient's validated time series for one test (cumulative view).</summary>
    Task<IReadOnlyList<CumulativePointDto>> CumulativeAsync(Guid patientId, string testCode, CancellationToken ct = default);
    /// <summary>Active (non-voided) results for one visit — drives the amendment panel (P09.5).</summary>
    Task<IReadOnlyList<VisitResultDto>> ForVisitAsync(Guid visitId, CancellationToken ct = default);
}

/// <summary>P09.1: test lines awaiting a result (samples received, no active result).</summary>
public sealed record GetPendingEntryQuery : IQuery<IReadOnlyList<PendingEntryDto>>, IRequirePermission
{
    public string Permission => "results.result.enter";
}

internal sealed class GetPendingEntryHandler : IRequestHandler<GetPendingEntryQuery, IReadOnlyList<PendingEntryDto>>
{
    private readonly IResultQueries _queries;
    public GetPendingEntryHandler(IResultQueries queries) => _queries = queries;
    public Task<IReadOnlyList<PendingEntryDto>> Handle(GetPendingEntryQuery request, CancellationToken ct) =>
        _queries.PendingEntryAsync(ct);
}

/// <summary>P09.2: entered results awaiting technical review.</summary>
public sealed record GetTechnicalQueueQuery : IQuery<IReadOnlyList<ResultQueueItemDto>>, IRequirePermission
{
    public string Permission => "results.result.validateTechnical";
}

internal sealed class GetTechnicalQueueHandler : IRequestHandler<GetTechnicalQueueQuery, IReadOnlyList<ResultQueueItemDto>>
{
    private readonly IResultQueries _queries;
    public GetTechnicalQueueHandler(IResultQueries queries) => _queries = queries;
    public Task<IReadOnlyList<ResultQueueItemDto>> Handle(GetTechnicalQueueQuery request, CancellationToken ct) =>
        _queries.TechnicalQueueAsync(ct);
}

/// <summary>P09.3: technically valid results awaiting medical sign-out.</summary>
public sealed record GetMedicalQueueQuery : IQuery<IReadOnlyList<ResultQueueItemDto>>, IRequirePermission
{
    public string Permission => "results.result.validateMedical";
}

internal sealed class GetMedicalQueueHandler : IRequestHandler<GetMedicalQueueQuery, IReadOnlyList<ResultQueueItemDto>>
{
    private readonly IResultQueries _queries;
    public GetMedicalQueueHandler(IResultQueries queries) => _queries = queries;
    public Task<IReadOnlyList<ResultQueueItemDto>> Handle(GetMedicalQueueQuery request, CancellationToken ct) =>
        _queries.MedicalQueueAsync(ct);
}

/// <summary>P09.4: open critical values with their communication state.</summary>
public sealed record GetCriticalQueueQuery : IQuery<IReadOnlyList<CriticalQueueItemDto>>, IRequirePermission
{
    public string Permission => "results.result.enter";
}

internal sealed class GetCriticalQueueHandler : IRequestHandler<GetCriticalQueueQuery, IReadOnlyList<CriticalQueueItemDto>>
{
    private readonly IResultQueries _queries;
    public GetCriticalQueueHandler(IResultQueries queries) => _queries = queries;
    public Task<IReadOnlyList<CriticalQueueItemDto>> Handle(GetCriticalQueueQuery request, CancellationToken ct) =>
        _queries.CriticalQueueAsync(ct);
}

/// <summary>Active results for a visit (order details / amendment panel).</summary>
public sealed record GetVisitResultsQuery(Guid VisitId) : IQuery<IReadOnlyList<VisitResultDto>>, IRequirePermission
{
    public string Permission => "orders.visit.read";
}

internal sealed class GetVisitResultsHandler : IRequestHandler<GetVisitResultsQuery, IReadOnlyList<VisitResultDto>>
{
    private readonly IResultQueries _queries;
    public GetVisitResultsHandler(IResultQueries queries) => _queries = queries;
    public Task<IReadOnlyList<VisitResultDto>> Handle(GetVisitResultsQuery request, CancellationToken ct) =>
        _queries.ForVisitAsync(request.VisitId, ct);
}

/// <summary>P10.3: cumulative trend for one patient + test (validated results only).</summary>
public sealed record GetCumulativeQuery(Guid PatientId, string TestCode)
    : IQuery<IReadOnlyList<CumulativePointDto>>, IRequirePermission
{
    public string Permission => "patients.patient.read";
}

internal sealed class GetCumulativeHandler : IRequestHandler<GetCumulativeQuery, IReadOnlyList<CumulativePointDto>>
{
    private readonly IResultQueries _queries;
    public GetCumulativeHandler(IResultQueries queries) => _queries = queries;
    public Task<IReadOnlyList<CumulativePointDto>> Handle(GetCumulativeQuery request, CancellationToken ct) =>
        _queries.CumulativeAsync(request.PatientId, request.TestCode, ct);
}
