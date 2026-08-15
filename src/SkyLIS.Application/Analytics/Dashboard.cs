using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Analytics;

/// <summary>FR-ANA-001 (P23.1): tenant-wide executive KPIs for the operational day.</summary>
public sealed record DashboardDto(
    DateOnly Day,
    int VisitsToday,
    int StatOpen,
    int InProcess,
    int AwaitingTechnicalValidation,
    int AwaitingMedicalValidation,
    int ReportedToday,
    int ReservedSamplesPending,
    int OpenCriticalValues,
    int RejectionsToday,
    decimal RevenueToday,
    string Currency,
    double? MedianRegisterToReportMinutes,
    IReadOnlyList<PipelineStageDto> Pipeline);

public sealed record PipelineStageDto(string Stage, int Count);

public sealed record GetDashboardQuery : IQuery<DashboardDto>, IRequirePermission
{
    public string Permission => "analytics.dashboard.read";
}

/// <summary>
/// Read port for M23. Current implementation queries the OLTP store directly (tenant-
/// scoped, bounded); the event-projection analytics store (SRS §10) replaces it behind
/// this same port when the messaging slice lands.
/// </summary>
public interface IAnalyticsQueries
{
    Task<DashboardDto> DashboardAsync(DateOnly day, CancellationToken ct = default);
}

internal sealed class GetDashboardHandler : IRequestHandler<GetDashboardQuery, DashboardDto>
{
    private readonly IAnalyticsQueries _queries;
    private readonly IClock _clock;

    public GetDashboardHandler(IAnalyticsQueries queries, IClock clock)
    {
        _queries = queries;
        _clock = clock;
    }

    public Task<DashboardDto> Handle(GetDashboardQuery request, CancellationToken ct) =>
        _queries.DashboardAsync(DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime), ct);
}
