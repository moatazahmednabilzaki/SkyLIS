using Microsoft.EntityFrameworkCore;
using SkyLIS.Application.Files;
using SkyLIS.Application.Platform;
using SkyLIS.Application.Search;

namespace SkyLIS.Infrastructure.Persistence;

internal sealed class MasterTestQueries : IMasterTestQueries
{
    private readonly SkyLisDbContext _db;
    public MasterTestQueries(SkyLisDbContext db) => _db = db;

    public async Task<IReadOnlyList<MasterTestDto>> ListAsync(CancellationToken ct = default) =>
        await _db.MasterTests.AsNoTracking()
            .OrderBy(m => m.Code)
            .Select(m => new MasterTestDto(
                m.Id, m.Code, m.Name, m.Department, m.SampleTypeName, m.ContainerName,
                m.ConditionName, m.CreatedAtUtc, m.LastPushedAtUtc, m.PushCount))
            .ToListAsync(ct);
}

internal sealed class AttachmentQueries : IAttachmentQueries
{
    private readonly SkyLisDbContext _db;
    public AttachmentQueries(SkyLisDbContext db) => _db = db;

    public async Task<IReadOnlyList<AttachmentDto>> ListAsync(
        string entityType, Guid entityId, CancellationToken ct = default)
    {
        var normalized = entityType.Trim().ToLowerInvariant();
        return await _db.Attachments.AsNoTracking()
            .Where(a => a.EntityType == normalized && a.EntityId == entityId)
            .OrderByDescending(a => a.UploadedAtUtc)
            .Select(a => new AttachmentDto(
                a.Id, a.EntityType, a.EntityId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedAtUtc))
            .ToListAsync(ct);
    }
}

internal sealed class SearchQueries : ISearchQueries
{
    private readonly SkyLisDbContext _db;
    public SearchQueries(SkyLisDbContext db) => _db = db;

    public async Task<GlobalSearchDto> SearchAsync(string term, CancellationToken ct = default)
    {
        const int top = 5;
        var like = $"%{term}%";

        var patients = await _db.Patients.AsNoTracking()
            .Where(p => EF.Functions.ILike(p.FullName, like)
                        || p.PatientNumber == term || p.NationalId == term)
            .OrderByDescending(p => p.LastVisitAtUtc)
            .Take(top)
            .Select(p => new SearchHitDto("patient", p.Id, p.FullName, p.PatientNumber, p.Id))
            .ToListAsync(ct);

        var visits = await _db.Visits.AsNoTracking()
            .Where(v => EF.Functions.ILike(v.VisitNumber, like))
            .OrderByDescending(v => v.RegisteredAtUtc)
            .Take(top)
            .Select(v => new SearchHitDto("visit", v.Id, v.VisitNumber, v.Status.ToString(), v.Id))
            .ToListAsync(ct);

        // Samples navigate to their visit (the actionable page).
        var samples = await _db.Visits.AsNoTracking()
            .SelectMany(v => v.Samples, (v, s) => new { v.Id, s.Barcode, s.State })
            .Where(x => EF.Functions.ILike(x.Barcode, like))
            .Take(top)
            .Select(x => new SearchHitDto("sample", x.Id, x.Barcode, x.State.ToString(), x.Id))
            .ToListAsync(ct);

        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => EF.Functions.ILike(i.InvoiceNumber, like))
            .OrderByDescending(i => i.IssuedAtUtc)
            .Take(top)
            .Select(i => new SearchHitDto("invoice", i.Id, i.InvoiceNumber, i.Status.ToString(), i.VisitId))
            .ToListAsync(ct);

        var tests = await _db.LabTests.AsNoTracking()
            .Where(t => EF.Functions.ILike(t.Code, like) || EF.Functions.ILike(t.Name, like))
            .OrderBy(t => t.Code)
            .Take(top)
            .Select(t => new SearchHitDto("test", t.Id, t.Code, t.Name, null))
            .ToListAsync(ct);

        return new GlobalSearchDto(patients, visits, samples, invoices, tests);
    }
}
