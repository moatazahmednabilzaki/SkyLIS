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
            .Select(p => new { p.Amount.Amount, p.Amount.Currency, p.IsRefund })
            .ToListAsync(ct);
        var revenue = payments.Sum(p => p.IsRefund ? -p.Amount : p.Amount);
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

    public async Task<AnalyticsDetailDto> DetailAsync(DateOnly fromDay, DateOnly toDay, CancellationToken ct = default)
    {
        var fromUtc = new DateTimeOffset(fromDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(toDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(1);

        // ---- P23.2: register -> medical sign-out TAT per test ----
        var tatRows = await _db.TestResults.AsNoTracking()
            .Where(r => r.Status == ResultStatus.MedicallyValid
                        && r.MedicallyValidatedAtUtc != null
                        && r.MedicallyValidatedAtUtc >= fromUtc && r.MedicallyValidatedAtUtc < toUtc)
            .Join(_db.Visits.AsNoTracking(), r => r.VisitId, v => v.Id,
                (r, v) => new { r.TestCode, r.MedicallyValidatedAtUtc, v.RegisteredAtUtc })
            .ToListAsync(ct);

        var departments = await _db.LabTests.AsNoTracking()
            .Select(t => new { t.Code, t.Department })
            .ToListAsync(ct);
        var departmentByCode = departments
            .GroupBy(t => t.Code).ToDictionary(g => g.Key, g => g.First().Department);

        var tat = tatRows
            .GroupBy(r => r.TestCode)
            .Select(g =>
            {
                var minutes = g.Select(r => (r.MedicallyValidatedAtUtc!.Value - r.RegisteredAtUtc).TotalMinutes)
                    .OrderBy(m => m).ToList();
                return new TatRowDto(
                    g.Key, departmentByCode.GetValueOrDefault(g.Key, "?"), minutes.Count,
                    Math.Round(Percentile(minutes, 0.5), 1), Math.Round(Percentile(minutes, 0.9), 1));
            })
            .OrderByDescending(r => r.Count)
            .ToList();

        // ---- P23.3: financial breakdown (net of refunds) ----
        var payments = await _db.Invoices.AsNoTracking()
            .SelectMany(i => i.Payments, (i, p) => new { i.BranchId, Payment = p })
            .Where(x => x.Payment.CapturedAtUtc >= fromUtc && x.Payment.CapturedAtUtc < toUtc)
            .Select(x => new
            {
                x.BranchId, Amount = x.Payment.Amount.Amount, x.Payment.Amount.Currency,
                x.Payment.Method, x.Payment.IsRefund, x.Payment.CapturedAtUtc,
            })
            .ToListAsync(ct);
        var branchCodes = await _db.Branches.AsNoTracking()
            .Select(b => new { b.Id, b.Code }).ToListAsync(ct);
        var codeById = branchCodes.ToDictionary(b => b.Id, b => b.Code);

        var totalCaptured = payments.Where(p => !p.IsRefund).Sum(p => p.Amount);
        var totalRefunded = payments.Where(p => p.IsRefund).Sum(p => p.Amount);
        var financial = new FinancialAnalysisDto(
            totalCaptured, totalRefunded, totalCaptured - totalRefunded,
            payments.Select(p => p.Currency).FirstOrDefault() ?? "EGP",
            payments.GroupBy(p => p.Method)
                .Select(g => new MoneyByKeyDto(g.Key, g.Sum(p => p.IsRefund ? -p.Amount : p.Amount)))
                .OrderByDescending(m => m.Amount).ToList(),
            payments.GroupBy(p => codeById.GetValueOrDefault(p.BranchId, "?"))
                .Select(g => new MoneyByKeyDto(g.Key, g.Sum(p => p.IsRefund ? -p.Amount : p.Amount)))
                .OrderByDescending(m => m.Amount).ToList(),
            payments.GroupBy(p => DateOnly.FromDateTime(p.CapturedAtUtc.UtcDateTime))
                .OrderBy(g => g.Key)
                .Select(g => new MoneyByKeyDto(
                    g.Key.ToString("yyyy-MM-dd"), g.Sum(p => p.IsRefund ? -p.Amount : p.Amount)))
                .ToList());

        // ---- P23.4: pre-analytic & analytic quality ----
        var samples = await _db.Visits.AsNoTracking()
            .SelectMany(v => v.Samples)
            .Where(s => s.State != SampleState.Reserved && s.State != SampleState.ConditionPending)
            .Select(s => new { s.State, s.RejectionReasonCode })
            .ToListAsync(ct);
        var rejected = samples.Where(s => s.State == SampleState.Rejected).ToList();
        var criticals = await _db.TestResults.AsNoTracking()
            .CountAsync(r => r.Critical != null, ct);
        var criticalsClosed = await _db.TestResults.AsNoTracking()
            .CountAsync(r => r.Critical != null && r.Critical.State == CriticalState.Closed, ct);
        var amended = await _db.TestResults.AsNoTracking().CountAsync(r => r.IsAmended, ct);
        var reruns = await _db.TestResults.AsNoTracking()
            .CountAsync(r => r.Status == ResultStatus.RerunOrdered, ct);

        var quality = new QualityAnalysisDto(
            samples.Count, rejected.Count,
            samples.Count == 0 ? 0 : Math.Round(100.0 * rejected.Count / samples.Count, 1),
            rejected.GroupBy(s => s.RejectionReasonCode ?? "?")
                .Select(g => new RejectionReasonDto(g.Key, g.Count()))
                .OrderByDescending(r => r.Count).ToList(),
            criticals, criticalsClosed, amended, reruns);

        return new AnalyticsDetailDto(fromDay, toDay, tat, financial, quality);
    }

    private static double Percentile(IReadOnlyList<double> ordered, double percentile)
    {
        if (ordered.Count == 0) return 0;
        var rank = percentile * (ordered.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return low == high ? ordered[low] : ordered[low] + (rank - low) * (ordered[high] - ordered[low]);
    }
}
