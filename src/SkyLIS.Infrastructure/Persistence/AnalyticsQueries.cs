using Microsoft.EntityFrameworkCore;
using SkyLIS.Application.Analytics;
using SkyLIS.Domain.Results;
using SkyLIS.Domain.Visits;

namespace SkyLIS.Infrastructure.Persistence;

internal sealed class AnalyticsQueries : IAnalyticsQueries
{
    private readonly SkyLisDbContext _db;
    public AnalyticsQueries(SkyLisDbContext db) => _db = db;

    public async Task<DashboardDto> DashboardAsync(DateOnly day, CancellationToken ct = default)
    {
        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        var visitAgg = await _db.Visits.AsNoTracking()
            .Where(v => v.RegisteredAtUtc >= dayStart && v.RegisteredAtUtc < dayEnd)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                StatOpen = g.Count(v => v.IsStat && v.Status != VisitStatus.Reported
                                                 && v.Status != VisitStatus.Closed
                                                 && v.Status != VisitStatus.Cancelled),
                InProcess = g.Count(v => v.Status == VisitStatus.InProcess),
                Reported = g.Count(v => v.Status == VisitStatus.Reported || v.Status == VisitStatus.Closed),
                Registered = g.Count(v => v.Status == VisitStatus.Registered),
                Collected = g.Count(v => v.Status == VisitStatus.Collected),
                Received = g.Count(v => v.Status == VisitStatus.Received),
                Validated = g.Count(v => v.Status == VisitStatus.Validated),
            })
            .FirstOrDefaultAsync(ct);

        var awaitingTech = await _db.TestResults.AsNoTracking()
            .CountAsync(r => r.Status == ResultStatus.Entered, ct);
        var awaitingMed = await _db.TestResults.AsNoTracking()
            .CountAsync(r => r.Status == ResultStatus.TechnicallyValid, ct);
        var openCriticals = await _db.TestResults.AsNoTracking()
            .CountAsync(r => r.Critical != null && r.Critical.State != CriticalState.Closed
                             && r.Status != ResultStatus.RerunOrdered, ct);

        var reservedPending = await _db.Visits.AsNoTracking()
            .SelectMany(v => v.Samples)
            .CountAsync(s => s.State == SampleState.ConditionPending, ct);
        var rejectionsToday = await _db.Visits.AsNoTracking()
            .SelectMany(v => v.Samples)
            .CountAsync(s => s.State == SampleState.Rejected
                             && s.CollectedAtUtc != null
                             && s.CollectedAtUtc >= dayStart && s.CollectedAtUtc < dayEnd, ct);

        var payments = await _db.Invoices.AsNoTracking()
            .SelectMany(i => i.Payments)
            .Where(p => p.CapturedAtUtc >= dayStart && p.CapturedAtUtc < dayEnd)
            .Select(p => new { p.Amount.Amount, p.Amount.Currency })
            .ToListAsync(ct);
        var revenue = payments.Sum(p => p.Amount);
        var currency = payments.Select(p => p.Currency).FirstOrDefault() ?? "EGP";

        // Register -> Reported TAT for today's reported visits (first final report render).
        var tatPairs = await _db.LabReports.AsNoTracking()
            .Where(r => r.Kind == Domain.Reports.ReportKind.Final
                        && r.RenderedAtUtc >= dayStart && r.RenderedAtUtc < dayEnd)
            .Join(_db.Visits.AsNoTracking(), r => r.VisitId, v => v.Id,
                (r, v) => new { v.RegisteredAtUtc, r.RenderedAtUtc })
            .ToListAsync(ct);
        var tatMinutes = tatPairs
            .Select(p => (p.RenderedAtUtc - p.RegisteredAtUtc).TotalMinutes)
            .ToList();
        double? medianTat = null;
        if (tatMinutes.Count > 0)
        {
            var ordered = tatMinutes.OrderBy(m => m).ToList();
            medianTat = ordered.Count % 2 == 1
                ? ordered[ordered.Count / 2]
                : (ordered[ordered.Count / 2 - 1] + ordered[ordered.Count / 2]) / 2.0;
        }

        return new DashboardDto(
            day,
            visitAgg?.Total ?? 0,
            visitAgg?.StatOpen ?? 0,
            visitAgg?.InProcess ?? 0,
            awaitingTech,
            awaitingMed,
            visitAgg?.Reported ?? 0,
            reservedPending,
            openCriticals,
            rejectionsToday,
            revenue,
            currency,
            medianTat,
            [
                new PipelineStageDto("Registered", visitAgg?.Registered ?? 0),
                new PipelineStageDto("Collected", visitAgg?.Collected ?? 0),
                new PipelineStageDto("Received", visitAgg?.Received ?? 0),
                new PipelineStageDto("InProcess", visitAgg?.InProcess ?? 0),
                new PipelineStageDto("Validated", visitAgg?.Validated ?? 0),
                new PipelineStageDto("Reported", visitAgg?.Reported ?? 0),
            ]);
    }
}
