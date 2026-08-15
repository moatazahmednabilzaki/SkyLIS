using SkyLIS.Domain.Common;

namespace SkyLIS.Application.Common;

/// <summary>
/// Real-time hint publisher (FR-SYS-010). Pushes are HINTS: clients react by reloading
/// from the system of record — never by trusting pushed payloads (EAA SignalR rule).
/// Implemented in the API host over the tenant-scoped hub groups.
/// </summary>
public interface IRealtimeNotifier
{
    Task WorklistChangedAsync(Guid tenantId, string area, CancellationToken ct = default);
}

/// <summary>
/// Generic outbox consumer forwarding a tenant event as a worklist-changed hint for the
/// configured areas. One registration per (event type, areas) pair — see AddApplication.
/// </summary>
internal sealed class RealtimeForwarder<TEvent> : IIntegrationEventHandler<TEvent>
    where TEvent : ITenantEvent
{
    private readonly IRealtimeNotifier _notifier;
    private readonly string[] _areas;

    public RealtimeForwarder(IRealtimeNotifier notifier, string[] areas)
    {
        _notifier = notifier;
        _areas = areas;
    }

    public async Task HandleAsync(TEvent domainEvent, CancellationToken ct = default)
    {
        foreach (var area in _areas)
            await _notifier.WorklistChangedAsync(domainEvent.TenantId, area, ct);
    }
}
