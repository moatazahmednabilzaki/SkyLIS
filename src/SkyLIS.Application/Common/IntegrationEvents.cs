using SkyLIS.Domain.Common;

namespace SkyLIS.Application.Common;

/// <summary>
/// Consumer of a published domain event (at-least-once delivery; the dispatcher's inbox
/// guarantees each handler processes each event at most once). Implementations must not
/// assume ordering across aggregates.
/// </summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct = default);
}

/// <summary>FR-SYS-011: per-tenant monthly usage counters feeding subscription billing (M01).</summary>
public interface IUsageMeterStore
{
    Task IncrementFinalizedReportsAsync(Guid tenantId, DateTimeOffset occurredAtUtc, CancellationToken ct = default);
    Task<IReadOnlyList<UsageMeterDto>> GetAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed record UsageMeterDto(Guid TenantId, int Year, int Month, int FinalizedReports);
