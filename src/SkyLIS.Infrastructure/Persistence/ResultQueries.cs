using Microsoft.EntityFrameworkCore;
using SkyLIS.Application.Common;
using SkyLIS.Application.Results;
using SkyLIS.Domain.Results;
using SkyLIS.Domain.Visits;

namespace SkyLIS.Infrastructure.Persistence;

internal sealed class TestResultRepository : ITestResultRepository
{
    private readonly SkyLisDbContext _db;
    public TestResultRepository(SkyLisDbContext db) => _db = db;

    public Task<TestResult?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.TestResults.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<TestResult?> GetActiveByLineAsync(Guid visitTestId, CancellationToken ct = default) =>
        _db.TestResults.FirstOrDefaultAsync(
            r => r.VisitTestId == visitTestId && r.Status != ResultStatus.RerunOrdered, ct);

    public void Add(TestResult result) => _db.TestResults.Add(result);
}

internal sealed class ResultQueries : IResultQueries
{
    private readonly SkyLisDbContext _db;
    public ResultQueries(SkyLisDbContext db) => _db = db;

    public Task<decimal?> GetPreviousValueAsync(Guid patientId, Guid testId, CancellationToken ct = default)
    {
        // Previous = latest medically valid value for the same test code on this patient.
        return _db.TestResults.AsNoTracking()
            .Where(r => r.PatientId == patientId
                        && r.Status == ResultStatus.MedicallyValid
                        && _db.Visits.SelectMany(v => v.Tests)
                            .Any(t => t.Id == r.VisitTestId && t.TestId == testId))
            .OrderByDescending(r => r.EnteredAtUtc)
            .Select(r => (decimal?)r.Value)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<PendingEntryDto>> PendingEntryAsync(CancellationToken ct = default)
    {
        var rows = await _db.Visits.AsNoTracking()
            .SelectMany(v => v.Tests, (v, t) => new { Visit = v, Line = t })
            .Where(x => (x.Line.Status == VisitTestStatus.Pending || x.Line.Status == VisitTestStatus.InProcess)
                        && x.Visit.Samples.Any(s => s.Id == x.Line.SampleId && s.State == SampleState.Received))
            .OrderByDescending(x => x.Visit.IsStat).ThenBy(x => x.Visit.RegisteredAtUtc)
            .Take(100)
            .Select(x => new
            {
                x.Visit.Id, x.Visit.VisitNumber, x.Visit.IsStat, x.Visit.PatientId,
                LineId = x.Line.Id, x.Line.TestCode, x.Line.TestId,
                Barcode = x.Visit.Samples.Where(s => s.Id == x.Line.SampleId).Select(s => s.Barcode).First(),
            })
            .ToListAsync(ct);

        var patientIds = rows.Select(r => r.PatientId).Distinct().ToList();
        var patients = await _db.Patients.AsNoTracking()
            .Where(p => patientIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName })
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct);

        var testIds = rows.Select(r => r.TestId).Distinct().ToList();
        var schemas = await _db.LabTests.AsNoTracking()
            .Where(t => testIds.Contains(t.Id) && t.ResultSchema != null)
            .Select(t => new { t.Id, t.ResultSchema!.Unit, t.ResultSchema.RefLow, t.ResultSchema.RefHigh })
            .ToDictionaryAsync(t => t.Id, ct);

        return rows.Select(r =>
        {
            var schema = schemas.GetValueOrDefault(r.TestId);
            return new PendingEntryDto(
                r.Id, r.VisitNumber, r.LineId, r.TestCode,
                patients.GetValueOrDefault(r.PatientId, "(unknown)"), r.Barcode, r.IsStat,
                schema?.Unit, schema?.RefLow, schema?.RefHigh, null);
        }).ToList();
    }

    public Task<IReadOnlyList<ResultQueueItemDto>> TechnicalQueueAsync(CancellationToken ct = default) =>
        QueueAsync(ResultStatus.Entered, ct);

    public Task<IReadOnlyList<ResultQueueItemDto>> MedicalQueueAsync(CancellationToken ct = default) =>
        QueueAsync(ResultStatus.TechnicallyValid, ct);

    private async Task<IReadOnlyList<ResultQueueItemDto>> QueueAsync(ResultStatus status, CancellationToken ct)
    {
        var rows = await _db.TestResults.AsNoTracking()
            .Where(r => r.Status == status)
            .OrderBy(r => r.EnteredAtUtc)
            .Take(200)
            .Select(r => new
            {
                r.Id, r.VisitId, r.PatientId, r.TestCode, r.Value, r.Unit,
                r.Flag, r.DeltaFlagged, r.PreviousValue, r.Status, r.EnteredAtUtc,
            })
            .ToListAsync(ct);
        var context = await VisitAndPatientNamesAsync(
            rows.Select(r => r.VisitId), rows.Select(r => r.PatientId), ct);

        return rows.Select(r => new ResultQueueItemDto(
            r.Id, r.VisitId, context.VisitNumbers.GetValueOrDefault(r.VisitId, "?"),
            context.PatientNames.GetValueOrDefault(r.PatientId, "(unknown)"), r.TestCode,
            r.Value, r.Unit, r.Flag.ToString(), r.DeltaFlagged, r.PreviousValue,
            r.Status.ToString(), r.EnteredAtUtc)).ToList();
    }

    public async Task<IReadOnlyList<CriticalQueueItemDto>> CriticalQueueAsync(CancellationToken ct = default)
    {
        var rows = await _db.TestResults.AsNoTracking()
            .Where(r => r.Critical != null)
            .OrderBy(r => r.Critical!.State).ThenBy(r => r.Critical!.FlaggedAtUtc)
            .Take(100)
            .Select(r => new
            {
                r.Id, r.VisitId, r.PatientId, r.TestCode, r.Value, r.Unit, r.Flag,
                r.Critical!.State, r.Critical.FlaggedAtUtc, r.Critical.CalledPerson, r.Critical.ReadBackConfirmed,
            })
            .ToListAsync(ct);
        var context = await VisitAndPatientNamesAsync(
            rows.Select(r => r.VisitId), rows.Select(r => r.PatientId), ct);

        return rows.Select(r => new CriticalQueueItemDto(
            r.Id, context.VisitNumbers.GetValueOrDefault(r.VisitId, "?"),
            context.PatientNames.GetValueOrDefault(r.PatientId, "(unknown)"), r.TestCode,
            r.Value, r.Unit, r.Flag.ToString(), r.State.ToString(),
            r.FlaggedAtUtc, r.CalledPerson, r.ReadBackConfirmed)).ToList();
    }

    private async Task<(Dictionary<Guid, string> VisitNumbers, Dictionary<Guid, string> PatientNames)>
        VisitAndPatientNamesAsync(IEnumerable<Guid> visitIds, IEnumerable<Guid> patientIds, CancellationToken ct)
    {
        var visitIdList = visitIds.Distinct().ToList();
        var patientIdList = patientIds.Distinct().ToList();
        var visitNumbers = await _db.Visits.AsNoTracking()
            .Where(v => visitIdList.Contains(v.Id))
            .Select(v => new { v.Id, v.VisitNumber })
            .ToDictionaryAsync(v => v.Id, v => v.VisitNumber, ct);
        var patientNames = await _db.Patients.AsNoTracking()
            .Where(p => patientIdList.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName })
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct);
        return (visitNumbers, patientNames);
    }
}
