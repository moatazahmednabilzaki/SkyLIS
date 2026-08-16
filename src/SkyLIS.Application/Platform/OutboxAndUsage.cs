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

public sealed record TenantUsageDto(
    string PlanCode, string? PlanName, int? MonthlyReportQuota, int? MaxUsers, int? MaxBranches,
    IReadOnlyList<UsageMeterDto> Months);

/// <summary>P01.3 metering explorer: monthly finalized-report counters against the plan quota.</summary>
public sealed record GetTenantUsageQuery(Guid TenantId)
    : IQuery<TenantUsageDto>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.tenant.read";
}

internal sealed class GetTenantUsageHandler : IRequestHandler<GetTenantUsageQuery, TenantUsageDto>
{
    private readonly IUsageMeterStore _meters;
    private readonly ITenantRepository _tenants;
    private readonly IPlanRepository _plans;

    public GetTenantUsageHandler(IUsageMeterStore meters, ITenantRepository tenants, IPlanRepository plans)
    {
        _meters = meters;
        _tenants = tenants;
        _plans = plans;
    }

    public async Task<TenantUsageDto> Handle(GetTenantUsageQuery request, CancellationToken ct)
    {
        var tenant = await _tenants.GetAsync(request.TenantId, ct)
            ?? throw new NotFoundException("Tenant", request.TenantId);
        var plan = await _plans.GetByCodeAsync(tenant.PlanCode, ct);
        var months = await _meters.GetAsync(request.TenantId, ct);
        return new TenantUsageDto(
            tenant.PlanCode, plan?.Name, plan?.MonthlyReportQuota, plan?.MaxUsers, plan?.MaxBranches, months);
    }
}
