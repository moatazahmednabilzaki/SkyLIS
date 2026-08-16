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

// ---------- P23.2–P23.4: TAT / financial / quality analysis (30-day window) ----------

public sealed record TatRowDto(
    string TestCode, string Department, int Count, double MedianMinutes, double P90Minutes);

public sealed record MoneyByKeyDto(string Key, decimal Amount);

public sealed record FinancialAnalysisDto(
    decimal TotalCaptured, decimal TotalRefunded, decimal NetRevenue, string Currency,
    IReadOnlyList<MoneyByKeyDto> ByMethod,
    IReadOnlyList<MoneyByKeyDto> ByBranch,
    IReadOnlyList<MoneyByKeyDto> ByDay);

public sealed record RejectionReasonDto(string ReasonCode, int Count);

public sealed record QualityAnalysisDto(
    int SamplesTotal, int SamplesRejected, double RejectionRatePercent,
    IReadOnlyList<RejectionReasonDto> ByReason,
    int CriticalValues, int CriticalsClosed, int AmendedResults, int RerunsOrdered);

public sealed record AnalyticsDetailDto(
    DateOnly FromDay, DateOnly ToDay,
    IReadOnlyList<TatRowDto> Tat,
    FinancialAnalysisDto Financial,
    QualityAnalysisDto Quality);

public sealed record GetAnalyticsDetailQuery : IQuery<AnalyticsDetailDto>, IRequirePermission
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
    Task<AnalyticsDetailDto> DetailAsync(DateOnly fromDay, DateOnly toDay, CancellationToken ct = default);
}

internal sealed class GetAnalyticsDetailHandler : IRequestHandler<GetAnalyticsDetailQuery, AnalyticsDetailDto>
{
    private readonly IAnalyticsQueries _queries;
    private readonly IClock _clock;

    public GetAnalyticsDetailHandler(IAnalyticsQueries queries, IClock clock)
    {
        _queries = queries;
        _clock = clock;
    }

    public Task<AnalyticsDetailDto> Handle(GetAnalyticsDetailQuery request, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        return _queries.DetailAsync(today.AddDays(-29), today, ct);
    }
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
