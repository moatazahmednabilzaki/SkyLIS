using Microsoft.EntityFrameworkCore;
using SkyLIS.Application.Audit;
using SkyLIS.Application.Common;
using SkyLIS.Infrastructure.Persistence;
using SkyLIS.Infrastructure.Tenancy;

namespace SkyLIS.Infrastructure.Audit;

internal sealed class AuditQueries : IAuditQueries
{
    private readonly SkyLisDbContext _db;
    private readonly TenantContext _tenant;

    public AuditQueries(SkyLisDbContext db, TenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<AuditEventDto>> SearchAsync(
        string? entityType, string? entityId, int take, CancellationToken ct = default)
    {
        var query = _db.AuditEvents.AsNoTracking()
            .Where(a => a.TenantId == _tenant.TenantId);
        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(entityId))
            query = query.Where(a => a.EntityId == entityId);

        return await query
            .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Take(take)
            .Select(a => new AuditEventDto(
                a.Id, a.Action, a.EntityType, a.EntityId, a.OldValues, a.NewValues,
                a.UserId, a.IpAddress, a.OccurredAtUtc, a.Hash, a.PreviousHash))
            .ToListAsync(ct);
    }

    public async Task<ChainVerificationDto> VerifyChainAsync(CancellationToken ct = default)
    {
        var events = await _db.AuditEvents.AsNoTracking()
            .Where(a => a.TenantId == _tenant.TenantId)
            .OrderBy(a => a.OccurredAtUtc).ThenBy(a => a.Id)
            .ToListAsync(ct);

        var previousHash = AuditEvent.GenesisHash;
        foreach (var auditEvent in events)
        {
            if (auditEvent.PreviousHash != previousHash)
                return new ChainVerificationDto(false, events.Count, auditEvent.Id.ToString(),
                    "Chain link mismatch: an event was removed, inserted, or reordered.");
            if (auditEvent.ComputeHash(previousHash) != auditEvent.Hash)
                return new ChainVerificationDto(false, events.Count, auditEvent.Id.ToString(),
                    "Payload hash mismatch: this event's content was modified after the fact.");
            previousHash = auditEvent.Hash;
        }
        return new ChainVerificationDto(true, events.Count, null, null);
    }
}
