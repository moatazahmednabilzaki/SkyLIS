using Microsoft.EntityFrameworkCore;
using SkyLIS.Application.Worklists;
using SkyLIS.Domain.Billing;
using SkyLIS.Domain.Reports;
using SkyLIS.Domain.Visits;

namespace SkyLIS.Infrastructure.Persistence;

internal sealed class WorklistQueries : IWorklistQueries
{
    private readonly SkyLisDbContext _db;
    public WorklistQueries(SkyLisDbContext db) => _db = db;

    public async Task<ReceptionWorklistDto> ReceptionAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var reservations = await ReservationsAsync(ct);

        var informationRows = await _db.Visits.AsNoTracking()
            .SelectMany(v => v.Samples, (v, s) => new { Visit = v, Sample = s })
            .Where(x => x.Sample.State == SampleState.Rejected && x.Sample.PatientInformedAtUtc == null)
            .OrderBy(x => x.Sample.CollectedAtUtc)
            .Take(50)
            .Select(x => new
            {
                x.Visit.Id, x.Visit.VisitNumber, x.Visit.PatientId,
                SampleId = x.Sample.Id, x.Sample.Barcode, x.Sample.RejectionReasonCode,
                Recollection = x.Visit.Samples
                    .Where(r => r.Barcode == x.Sample.Barcode + "R")
                    .Select(r => r.Barcode).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var handouts = await _db.LabReports.AsNoTracking()
            .Where(r => r.Status == ReportStatus.Rendered)
            .OrderBy(r => r.RenderedAtUtc)
            .Take(50)
            .Join(_db.Visits.AsNoTracking(), r => r.VisitId, v => v.Id, (r, v) => new
            {
                r.Id, r.ReportNumber, r.Kind, r.RenderedAtUtc, v.VisitNumber, v.PatientId,
            })
            .ToListAsync(ct);

        var balances = await _db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid)
            .OrderBy(i => i.IssuedAtUtc)
            .Take(50)
            .Join(_db.Visits.AsNoTracking(), i => i.VisitId, v => v.Id, (i, v) => new
            {
                i.Id, i.InvoiceNumber, v.VisitNumber, v.PatientId,
                Total = i.Total.Amount, i.Total.Currency,
                Paid = i.Payments.Sum(p => p.Amount.Amount),
            })
            .ToListAsync(ct);

        var patientNames = await PatientNamesAsync(
            informationRows.Select(r => r.PatientId)
                .Concat(handouts.Select(h => h.PatientId))
                .Concat(balances.Select(b => b.PatientId))
                .Concat(reservations.Select(r => r.PatientId)), ct);

        return new ReceptionWorklistDto(
            reservations.Select(r => new ReservationDueDto(
                r.VisitId, r.SampleId, r.Barcode, r.VisitNumber,
                patientNames.GetValueOrDefault(r.PatientId, "(unknown)"),
                r.Condition, r.ReadyAtUtc, r.ReadyAtUtc <= nowUtc)).ToList(),
            informationRows.Select(r => new PatientInformationDto(
                r.Id, r.SampleId, r.Barcode, r.VisitNumber,
                patientNames.GetValueOrDefault(r.PatientId, "(unknown)"),
                r.RejectionReasonCode ?? "?", r.Recollection)).ToList(),
            handouts.Select(h => new ReportHandoutDto(
                h.Id, h.ReportNumber, h.VisitNumber,
                patientNames.GetValueOrDefault(h.PatientId, "(unknown)"),
                h.Kind.ToString(), h.RenderedAtUtc)).ToList(),
            balances.Where(b => b.Total - b.Paid > 0).Select(b => new BalanceDueDto(
                b.Id, b.InvoiceNumber, b.VisitNumber,
                patientNames.GetValueOrDefault(b.PatientId, "(unknown)"),
                b.Total - b.Paid, b.Currency)).ToList());
    }

    public async Task<PhlebotomistWorklistDto> PhlebotomistAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var toCollect = await _db.Visits.AsNoTracking()
            .SelectMany(v => v.Samples, (v, s) => new { Visit = v, Sample = s })
            .Where(x => x.Sample.State == SampleState.ReadyToCollect
                        || (x.Sample.State == SampleState.ConditionPending && x.Sample.ConditionReadyAtUtc <= nowUtc))
            .OrderByDescending(x => x.Visit.IsStat).ThenBy(x => x.Visit.RegisteredAtUtc)
            .Take(100)
            .Select(x => new
            {
                x.Visit.Id, x.Visit.VisitNumber, x.Visit.PatientId, x.Visit.IsStat,
                SampleId = x.Sample.Id, x.Sample.Barcode, x.Sample.ConditionName,
            })
            .ToListAsync(ct);

        var reservations = await ReservationsAsync(ct);
        var upcoming = reservations.Where(r => r.ReadyAtUtc > nowUtc).ToList();

        var patientNames = await PatientNamesAsync(
            toCollect.Select(c => c.PatientId).Concat(upcoming.Select(u => u.PatientId)), ct);

        return new PhlebotomistWorklistDto(
            toCollect.Select(c => new CollectionItemDto(
                c.Id, c.SampleId, c.Barcode, c.VisitNumber,
                patientNames.GetValueOrDefault(c.PatientId, "(unknown)"),
                c.IsStat, c.Barcode.EndsWith("R"), c.ConditionName)).ToList(),
            upcoming.Select(r => new ReservationDueDto(
                r.VisitId, r.SampleId, r.Barcode, r.VisitNumber,
                patientNames.GetValueOrDefault(r.PatientId, "(unknown)"),
                r.Condition, r.ReadyAtUtc, WindowOpen: false)).ToList());
    }

    private sealed record ReservationRow(
        Guid VisitId, Guid SampleId, string Barcode, string VisitNumber, Guid PatientId,
        string? Condition, DateTimeOffset ReadyAtUtc);

    private async Task<List<ReservationRow>> ReservationsAsync(CancellationToken ct) =>
        await _db.Visits.AsNoTracking()
            .SelectMany(v => v.Samples, (v, s) => new { Visit = v, Sample = s })
            .Where(x => x.Sample.State == SampleState.ConditionPending)
            .OrderBy(x => x.Sample.ConditionReadyAtUtc)
            .Take(50)
            .Select(x => new ReservationRow(
                x.Visit.Id, x.Sample.Id, x.Sample.Barcode, x.Visit.VisitNumber,
                x.Visit.PatientId, x.Sample.ConditionName, x.Sample.ConditionReadyAtUtc!.Value))
            .ToListAsync(ct);

    private async Task<Dictionary<Guid, string>> PatientNamesAsync(IEnumerable<Guid> patientIds, CancellationToken ct)
    {
        var ids = patientIds.Distinct().ToList();
        return await _db.Patients.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName })
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct);
    }
}
