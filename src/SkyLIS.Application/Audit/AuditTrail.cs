using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Audit;

public sealed record AuditEventDto(
    Guid Id, string Action, string EntityType, string EntityId,
    string? OldValues, string? NewValues, Guid? UserId, string? IpAddress,
    DateTimeOffset OccurredAtUtc, string Hash, string PreviousHash);

public sealed record ChainVerificationDto(bool Valid, int EventCount, string? FirstBrokenEventId, string? Detail);

/// <summary>Read port for the audit explorer and the tamper-evidence verification.</summary>
public interface IAuditQueries
{
    Task<IReadOnlyList<AuditEventDto>> SearchAsync(string? entityType, string? entityId, int take, CancellationToken ct = default);
    Task<ChainVerificationDto> VerifyChainAsync(CancellationToken ct = default);
}

/// <summary>FR-SYS-001: searchable audit explorer (Quality module view).</summary>
public sealed record SearchAuditQuery(string? EntityType, string? EntityId, int Take = 50)
    : IQuery<IReadOnlyList<AuditEventDto>>, IRequirePermission
{
    public string Permission => "audit.trail.read";
}

internal sealed class SearchAuditHandler : IRequestHandler<SearchAuditQuery, IReadOnlyList<AuditEventDto>>
{
    private readonly IAuditQueries _queries;
    public SearchAuditHandler(IAuditQueries queries) => _queries = queries;
    public Task<IReadOnlyList<AuditEventDto>> Handle(SearchAuditQuery request, CancellationToken ct) =>
        _queries.SearchAsync(request.EntityType, request.EntityId, Math.Clamp(request.Take, 1, 500), ct);
}

/// <summary>
/// Recomputes the tenant's whole hash chain and compares it to the stored links —
/// any retroactive edit, deletion, or insertion surfaces as a break.
/// </summary>
public sealed record VerifyAuditChainQuery : IQuery<ChainVerificationDto>, IRequirePermission
{
    public string Permission => "audit.trail.read";
}

internal sealed class VerifyAuditChainHandler : IRequestHandler<VerifyAuditChainQuery, ChainVerificationDto>
{
    private readonly IAuditQueries _queries;
    public VerifyAuditChainHandler(IAuditQueries queries) => _queries = queries;
    public Task<ChainVerificationDto> Handle(VerifyAuditChainQuery request, CancellationToken ct) =>
        _queries.VerifyChainAsync(ct);
}
