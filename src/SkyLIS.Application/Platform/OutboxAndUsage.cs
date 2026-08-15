using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Platform;

public sealed record OutboxStatusDto(
    int Pending, int Processed, int Poisoned, IReadOnlyList<OutboxFailureDto> RecentFailures);

public sealed record OutboxFailureDto(Guid Id, string EventType, int Attempts, string? LastError, DateTimeOffset OccurredAtUtc);

public interface IOutboxStatusQueries
{
    Task<OutboxStatusDto> StatusAsync(CancellationToken ct = default);
}

/// <summary>Platform ops: outbox health (FR-SYS-010 monitored background processing).</summary>
public sealed record GetOutboxStatusQuery : IQuery<OutboxStatusDto>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.outbox.read";
}

internal sealed class GetOutboxStatusHandler : IRequestHandler<GetOutboxStatusQuery, OutboxStatusDto>
{
    private readonly IOutboxStatusQueries _queries;
    public GetOutboxStatusHandler(IOutboxStatusQueries queries) => _queries = queries;
    public Task<OutboxStatusDto> Handle(GetOutboxStatusQuery request, CancellationToken ct) =>
        _queries.StatusAsync(ct);
}

/// <summary>P01.3 metering explorer: the tenant's monthly finalized-report counters.</summary>
public sealed record GetTenantUsageQuery(Guid TenantId)
    : IQuery<IReadOnlyList<UsageMeterDto>>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.tenant.read";
}

internal sealed class GetTenantUsageHandler : IRequestHandler<GetTenantUsageQuery, IReadOnlyList<UsageMeterDto>>
{
    private readonly IUsageMeterStore _meters;
    public GetTenantUsageHandler(IUsageMeterStore meters) => _meters = meters;
    public Task<IReadOnlyList<UsageMeterDto>> Handle(GetTenantUsageQuery request, CancellationToken ct) =>
        _meters.GetAsync(request.TenantId, ct);
}
