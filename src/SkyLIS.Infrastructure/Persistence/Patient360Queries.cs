using Microsoft.EntityFrameworkCore;
using SkyLIS.Application.Patients;

namespace SkyLIS.Infrastructure.Persistence;

internal sealed class Patient360Queries : IPatient360Queries
{
    private readonly SkyLisDbContext _db;
    public Patient360Queries(SkyLisDbContext db) => _db = db;

    public async Task<Patient360Dto?> GetAsync(Guid patientId, DateOnly today, CancellationToken ct = default)
    {
        var patient = await _db.Patients.AsNoTracking()
            .Where(p => p.Id == patientId)
            .Select(p => new
            {
                p.Id, p.PatientNumber, p.FullName, p.Sex, p.DateOfBirth,
                p.Mobile, p.NationalId, p.RegisteredAtUtc, p.LastVisitAtUtc,
            })
            .FirstOrDefaultAsync(ct);
        if (patient is null) return null;

        var visits = await _db.Visits.AsNoTracking()
            .Where(v => v.PatientId == patientId)
            .OrderByDescending(v => v.RegisteredAtUtc)
            .Select(v => new { v.Id, v.VisitNumber, v.BranchId, v.RegisteredAtUtc, v.Status, v.IsStat })
            .ToListAsync(ct);
        var visitIds = visits.Select(v => v.Id).ToList();

        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => visitIds.Contains(i.VisitId))
            .Select(i => new
            {
                i.Id, i.VisitId, i.Status,
                Total = i.Total.Amount, i.Total.Currency,
                i.DiscountAmount, i.CreditedAmount,
                Paid = i.Payments.Where(p => !p.IsRefund).Sum(p => p.Amount.Amount),
                Refunded = i.Payments.Where(p => p.IsRefund).Sum(p => p.Amount.Amount),
            })
            .ToListAsync(ct);
        var invoiceByVisit = invoices.ToDictionary(i => i.VisitId);

        var branchCodes = await _db.Branches.AsNoTracking()
            .Select(b => new { b.Id, b.Code })
            .ToDictionaryAsync(b => b.Id, b => b.Code, ct);

        var reports = await _db.LabReports.AsNoTracking()
            .Where(r => r.PatientId == patientId)
            .OrderByDescending(r => r.RenderedAtUtc)
            .Take(50)
            .Select(r => new Patient360ReportDto(
                r.Id, r.ReportNumber, r.Version, r.Kind.ToString(), r.Status.ToString(), r.RenderedAtUtc))
            .ToListAsync(ct);

        var testCodes = await _db.TestResults.AsNoTracking()
            .Where(r => r.PatientId == patientId && r.Status == Domain.Results.ResultStatus.MedicallyValid)
            .Select(r => r.TestCode)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

        var age = today.Year - patient.DateOfBirth.Year;
        if (today < patient.DateOfBirth.AddYears(age)) age--;

        var visitDtos = visits.Select(v =>
        {
            var invoice = invoiceByVisit.GetValueOrDefault(v.Id);
            var balance = invoice is null ? 0
                : invoice.Total - invoice.DiscountAmount - invoice.CreditedAmount - (invoice.Paid - invoice.Refunded);
            return new Patient360VisitDto(
                v.Id, v.VisitNumber, branchCodes.GetValueOrDefault(v.BranchId, "?"), v.RegisteredAtUtc,
                v.Status.ToString(), v.IsStat, invoice?.Id ?? Guid.Empty,
                invoice?.Status.ToString() ?? "?", invoice?.Total ?? 0, balance,
                invoice?.Currency ?? "EGP");
        }).ToList();

        return new Patient360Dto(
            patient.Id, patient.PatientNumber, patient.FullName, patient.Sex.ToString(),
            patient.DateOfBirth, age, patient.Mobile.Value, patient.NationalId,
            patient.RegisteredAtUtc, patient.LastVisitAtUtc,
            visitDtos.Where(v => v.Status != "Cancelled").Sum(v => v.Balance),
            visitDtos.FirstOrDefault()?.Currency ?? "EGP",
            visitDtos, reports, testCodes);
    }
}
