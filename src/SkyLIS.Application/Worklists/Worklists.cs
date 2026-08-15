using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Worklists;

// ---- Reception worklist (P08.1, merged per SRS Rev 2.0) ----

public sealed record ReceptionWorklistDto(
    IReadOnlyList<ReservationDueDto> ReservationsDue,
    IReadOnlyList<PatientInformationDto> PatientInformation,
    IReadOnlyList<ReportHandoutDto> ReportsToHandOut,
    IReadOnlyList<BalanceDueDto> BalancesDue);

public sealed record ReservationDueDto(
    Guid VisitId, Guid SampleId, string Barcode, string VisitNumber, string PatientName,
    string? Condition, DateTimeOffset ReadyAtUtc, bool WindowOpen);

public sealed record PatientInformationDto(
    Guid VisitId, Guid SampleId, string Barcode, string VisitNumber, string PatientName,
    string ReasonCode, string? RecollectionBarcode);

public sealed record ReportHandoutDto(
    Guid ReportId, string ReportNumber, string VisitNumber, string PatientName, string Kind,
    DateTimeOffset RenderedAtUtc);

public sealed record BalanceDueDto(
    Guid InvoiceId, string InvoiceNumber, string VisitNumber, string PatientName,
    decimal Balance, string Currency);

// ---- Phlebotomist worklist (P08.2, merged) ----

public sealed record PhlebotomistWorklistDto(
    IReadOnlyList<CollectionItemDto> ToCollect,
    IReadOnlyList<ReservationDueDto> UpcomingReservations);

public sealed record CollectionItemDto(
    Guid VisitId, Guid SampleId, string Barcode, string VisitNumber, string PatientName,
    bool IsStat, bool IsRecollection, string? Condition);

public interface IWorklistQueries
{
    Task<ReceptionWorklistDto> ReceptionAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
    Task<PhlebotomistWorklistDto> PhlebotomistAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
}

public sealed record GetReceptionWorklistQuery : IQuery<ReceptionWorklistDto>, IRequirePermission
{
    public string Permission => "orders.visit.read";
}

internal sealed class GetReceptionWorklistHandler : IRequestHandler<GetReceptionWorklistQuery, ReceptionWorklistDto>
{
    private readonly IWorklistQueries _queries;
    private readonly IClock _clock;

    public GetReceptionWorklistHandler(IWorklistQueries queries, IClock clock)
    {
        _queries = queries;
        _clock = clock;
    }

    public Task<ReceptionWorklistDto> Handle(GetReceptionWorklistQuery request, CancellationToken ct) =>
        _queries.ReceptionAsync(_clock.UtcNow, ct);
}

public sealed record GetPhlebotomistWorklistQuery : IQuery<PhlebotomistWorklistDto>, IRequirePermission
{
    public string Permission => "samples.sample.collect";
}

internal sealed class GetPhlebotomistWorklistHandler
    : IRequestHandler<GetPhlebotomistWorklistQuery, PhlebotomistWorklistDto>
{
    private readonly IWorklistQueries _queries;
    private readonly IClock _clock;

    public GetPhlebotomistWorklistHandler(IWorklistQueries queries, IClock clock)
    {
        _queries = queries;
        _clock = clock;
    }

    public Task<PhlebotomistWorklistDto> Handle(GetPhlebotomistWorklistQuery request, CancellationToken ct) =>
        _queries.PhlebotomistAsync(_clock.UtcNow, ct);
}

/// <summary>P07.3: the mandatory patient-information step (who informed is in the audit trail).</summary>
public sealed record MarkPatientInformedCommand(Guid VisitId, Guid SampleId) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "samples.sample.informPatient";
}

internal sealed class MarkPatientInformedHandler : IRequestHandler<MarkPatientInformedCommand, Unit>
{
    private readonly IVisitRepository _visits;
    private readonly IClock _clock;

    public MarkPatientInformedHandler(IVisitRepository visits, IClock clock)
    {
        _visits = visits;
        _clock = clock;
    }

    public async Task<Unit> Handle(MarkPatientInformedCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetAsync(request.VisitId, ct)
            ?? throw new NotFoundException("Visit", request.VisitId);
        visit.MarkPatientInformed(request.SampleId, _clock.UtcNow);
        return Unit.Value;
    }
}
