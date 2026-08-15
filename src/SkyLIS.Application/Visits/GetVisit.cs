using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Visits;

public sealed record VisitDetailsDto(
    Guid Id, string VisitNumber, string Status, bool IsStat,
    Guid PatientId, string PatientName, DateTimeOffset RegisteredAtUtc,
    IReadOnlyList<VisitTestLineDto> Tests, IReadOnlyList<VisitSampleDto> Samples);

public sealed record VisitTestLineDto(Guid Id, string TestCode, string Status, decimal Price, string Currency, Guid SampleId);

public sealed record VisitSampleDto(
    Guid Id, string Barcode, string State, string? Condition,
    DateTimeOffset? ReadyAtUtc, string? RejectionReasonCode);

/// <summary>FR-ORD-020: single source of truth for one visit (order details).</summary>
public sealed record GetVisitQuery(Guid VisitId) : IQuery<VisitDetailsDto>, IRequirePermission
{
    public string Permission => "orders.visit.read";
}

public interface IVisitQueries
{
    Task<VisitDetailsDto?> GetAsync(Guid visitId, CancellationToken ct = default);
    Task<IReadOnlyList<VisitSampleDto>> ReservationsDueAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
}

internal sealed class GetVisitHandler : IRequestHandler<GetVisitQuery, VisitDetailsDto>
{
    private readonly IVisitQueries _queries;

    public GetVisitHandler(IVisitQueries queries) => _queries = queries;

    public async Task<VisitDetailsDto> Handle(GetVisitQuery request, CancellationToken ct) =>
        await _queries.GetAsync(request.VisitId, ct)
        ?? throw new NotFoundException("Visit", request.VisitId);
}
