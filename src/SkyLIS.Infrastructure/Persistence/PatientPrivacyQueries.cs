using Microsoft.EntityFrameworkCore;
using SkyLIS.Application.Patients;

namespace SkyLIS.Infrastructure.Persistence;

internal sealed class PatientPrivacyQueries : IPatientPrivacyQueries
{
    private readonly SkyLisDbContext _db;
    public PatientPrivacyQueries(SkyLisDbContext db) => _db = db;

    public async Task<IReadOnlyList<DuplicateGroupDto>> FindDuplicatesAsync(CancellationToken ct = default)
    {
        // Candidates share a mobile number OR (full name + date of birth). Merged and
        // erased records never appear.
        var patients = await _db.Patients.AsNoTracking()
            .Where(p => p.MergedIntoPatientId == null && !p.IsErased)
            .Select(p => new
            {
                p.Id, p.PatientNumber, p.FullName, p.Mobile, p.DateOfBirth, p.LastVisitAtUtc,
            })
            .ToListAsync(ct);

        var visitCounts = await _db.Visits.AsNoTracking()
            .GroupBy(v => v.PatientId)
            .Select(g => new { PatientId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PatientId, g => g.Count, ct);

        var candidates = patients.Select(p => new DuplicateCandidateDto(
            p.Id, p.PatientNumber, p.FullName, p.Mobile.Value, p.DateOfBirth,
            p.LastVisitAtUtc, visitCounts.GetValueOrDefault(p.Id, 0))).ToList();

        var byMobile = candidates
            .GroupBy(p => p.Mobile)
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroupDto($"mobile {g.Key}", g.ToList()));

        var byNameDob = candidates
            .GroupBy(p => (Name: p.FullName.ToLowerInvariant(), p.DateOfBirth))
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroupDto($"name+dob {g.First().FullName}", g.ToList()));

        // Groups matched by both keys collapse to one (same member ids).
        return byMobile.Concat(byNameDob)
            .GroupBy(g => string.Join("|", g.Patients.Select(p => p.Id).OrderBy(id => id)))
            .Select(g => g.First())
            .ToList();
    }

    public async Task<object?> ExportAsync(Guid patientId, CancellationToken ct = default)
    {
        var patient = await _db.Patients.AsNoTracking()
            .Where(p => p.Id == patientId)
            .Select(p => new
            {
                p.Id, p.PatientNumber, p.FullName, Sex = p.Sex.ToString(), p.DateOfBirth,
                Mobile = p.Mobile.Value, p.NationalId, p.RegisteredAtUtc, p.LastVisitAtUtc,
            })
            .FirstOrDefaultAsync(ct);
        if (patient is null) return null;

        var visits = await _db.Visits.AsNoTracking()
            .Where(v => v.PatientId == patientId)
            .OrderBy(v => v.RegisteredAtUtc)
            .Select(v => new
            {
                v.VisitNumber, Status = v.Status.ToString(), v.RegisteredAtUtc,
                Tests = v.Tests.Select(t => new { t.TestCode, Status = t.Status.ToString() }).ToList(),
            })
            .ToListAsync(ct);

        var results = await _db.TestResults.AsNoTracking()
            .Where(r => r.PatientId == patientId && r.Status == Domain.Results.ResultStatus.MedicallyValid)
            .OrderBy(r => r.MedicallyValidatedAtUtc)
            .Select(r => new
            {
                r.TestCode, r.Value, r.Unit, Flag = r.Flag.ToString(),
                r.MedicallyValidatedAtUtc, r.IsAmended, r.InterpretiveComment,
            })
            .ToListAsync(ct);

        var reports = await _db.LabReports.AsNoTracking()
            .Where(r => r.PatientId == patientId)
            .OrderBy(r => r.RenderedAtUtc)
            .Select(r => new
            {
                r.ReportNumber, r.Version, Kind = r.Kind.ToString(), r.ContentHash, r.RenderedAtUtc,
            })
            .ToListAsync(ct);

        return new
        {
            exportedAtUtc = DateTimeOffset.UtcNow,
            format = "SkyLIS data-subject export v1 (P04.5)",
            patient,
            visits,
            results,
            reports,
        };
    }

    public async Task<IReadOnlyList<DataSubjectRequestDto>> ListRequestsAsync(CancellationToken ct = default)
    {
        var requests = await _db.DataSubjectRequests.AsNoTracking()
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(100)
            .Select(r => new
            {
                r.Id, r.PatientId, Kind = r.Kind.ToString(), Status = r.Status.ToString(),
                r.Reason, r.CreatedAtUtc, r.DecidedAtUtc,
            })
            .ToListAsync(ct);

        var patientIds = requests.Select(r => r.PatientId).Distinct().ToList();
        var numbers = await _db.Patients.AsNoTracking()
            .Where(p => patientIds.Contains(p.Id))
            .Select(p => new { p.Id, p.PatientNumber })
            .ToDictionaryAsync(p => p.Id, p => p.PatientNumber, ct);

        return requests.Select(r => new DataSubjectRequestDto(
            r.Id, r.PatientId, numbers.GetValueOrDefault(r.PatientId, "?"),
            r.Kind, r.Status, r.Reason, r.CreatedAtUtc, r.DecidedAtUtc)).ToList();
    }
}
