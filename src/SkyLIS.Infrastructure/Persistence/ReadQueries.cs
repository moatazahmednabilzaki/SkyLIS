using Microsoft.EntityFrameworkCore;
using SkyLIS.Application.Patients;
using SkyLIS.Application.Tenants;
using SkyLIS.Application.Visits;

namespace SkyLIS.Infrastructure.Persistence;

// Read side: direct DTO projections; never loads full aggregates for lists (EAA rule).

internal sealed class TenantQueries : ITenantQueries
{
    private readonly SkyLisDbContext _db;
    public TenantQueries(SkyLisDbContext db) => _db = db;

    public async Task<IReadOnlyList<TenantDto>> ListAsync(string? search, CancellationToken ct = default)
    {
        var query = _db.Tenants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(t =>
                EF.Functions.ILike(t.LegalName, term) || EF.Functions.ILike(t.Subdomain, term));
        }
        return await query
            .OrderBy(t => t.LegalName)
            .Select(t => new TenantDto(
                t.Id, t.LegalName, t.Subdomain, t.CountryCode,
                t.PlanCode, t.Status.ToString(), t.CreatedAtUtc))
            .ToListAsync(ct);
    }
}

internal sealed class PatientQueries : IPatientQueries
{
    private readonly SkyLisDbContext _db;
    public PatientQueries(SkyLisDbContext db) => _db = db;

    public async Task<IReadOnlyList<PatientSearchResultDto>> SearchAsync(
        string term, DateOnly today, CancellationToken ct = default)
    {
        var like = $"%{term}%";
        // Mobile search: normalize the term to digits and match by equality on the
        // converted column (value-converted properties translate for equality only).
        Domain.Common.PhoneNumber? mobileTerm = null;
        var digitCount = term.Count(char.IsDigit);
        if (digitCount >= 8)
        {
            try { mobileTerm = Domain.Common.PhoneNumber.Of(term); }
            catch (Domain.Common.DomainException) { /* not a phone number — skip mobile matching */ }
        }

        var rows = await _db.Patients.AsNoTracking()
            // Merged duplicates and erased records never surface in search (P04.4/P04.5).
            .Where(p => p.MergedIntoPatientId == null && !p.IsErased)
            .Where(p =>
                EF.Functions.ILike(p.FullName, like) ||
                p.PatientNumber == term ||
                p.NationalId == term ||
                (mobileTerm != null && p.Mobile == mobileTerm))
            .OrderByDescending(p => p.LastVisitAtUtc)
            .Take(20)
            .Select(p => new
            {
                p.Id, p.PatientNumber, p.FullName,
                p.Mobile, // materialized via the value converter; .Value read client-side
                p.Sex, p.DateOfBirth, p.LastVisitAtUtc,
            })
            .ToListAsync(ct);

        return rows.Select(p =>
        {
            var age = today.Year - p.DateOfBirth.Year;
            if (today < p.DateOfBirth.AddYears(age)) age--;
            var mobile = p.Mobile.Value;
            var masked = mobile.Length > 4
                ? new string('•', mobile.Length - 4) + mobile[^4..]
                : mobile;
            return new PatientSearchResultDto(
                p.Id, p.PatientNumber, p.FullName, masked, p.Sex.ToString(), age, p.LastVisitAtUtc);
        }).ToList();
    }
}

internal sealed class VisitQueries : IVisitQueries
{
    private readonly SkyLisDbContext _db;
    public VisitQueries(SkyLisDbContext db) => _db = db;

    public async Task<VisitDetailsDto?> GetAsync(Guid visitId, CancellationToken ct = default)
    {
        var visit = await _db.Visits.AsNoTracking()
            .Where(v => v.Id == visitId)
            .Select(v => new
            {
                v.Id, v.VisitNumber, v.Status, v.IsStat, v.PatientId, v.RegisteredAtUtc,
                Tests = v.Tests.Select(t => new
                {
                    t.Id, t.TestCode, t.Status,
                    Amount = t.Price.Amount, t.Price.Currency, t.SampleId,
                }).ToList(),
                Samples = v.Samples.Select(s => new
                {
                    s.Id, s.Barcode, s.State, s.ConditionName, s.ConditionReadyAtUtc, s.RejectionReasonCode,
                }).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (visit is null) return null;

        var patientName = await _db.Patients.AsNoTracking()
            .Where(p => p.Id == visit.PatientId)
            .Select(p => p.FullName)
            .FirstOrDefaultAsync(ct) ?? "(unknown)";

        return new VisitDetailsDto(
            visit.Id, visit.VisitNumber, visit.Status.ToString(), visit.IsStat,
            visit.PatientId, patientName, visit.RegisteredAtUtc,
            visit.Tests.Select(t => new VisitTestLineDto(
                t.Id, t.TestCode, t.Status.ToString(), t.Amount, t.Currency, t.SampleId)).ToList(),
            visit.Samples.Select(s => new VisitSampleDto(
                s.Id, s.Barcode, s.State.ToString(), s.ConditionName,
                s.ConditionReadyAtUtc, s.RejectionReasonCode)).ToList());
    }

    public async Task<IReadOnlyList<VisitSampleDto>> ReservationsDueAsync(
        DateTimeOffset nowUtc, CancellationToken ct = default) =>
        await _db.Visits.AsNoTracking()
            .SelectMany(v => v.Samples)
            .Where(s => s.State == Domain.Visits.SampleState.ConditionPending && s.ConditionReadyAtUtc <= nowUtc)
            .OrderBy(s => s.ConditionReadyAtUtc)
            .Select(s => new VisitSampleDto(
                s.Id, s.Barcode, s.State.ToString(), s.ConditionName,
                s.ConditionReadyAtUtc, s.RejectionReasonCode))
            .ToListAsync(ct);
}
