using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SkyLIS.Application.Common;

namespace SkyLIS.Api.Infrastructure;

/// <summary>
/// Tenant-scoped worklist hub (FR-SYS-010). EAA SignalR rules applied:
/// every connection is authenticated (JWT), group membership is SERVER-assigned from the
/// token's tenant claim (clients can never choose a group), and messages carry only the
/// changed area name — hints, never data. Clients reload from the API (system of record).
/// </summary>
[Authorize]
public sealed class WorklistHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantClaim = Context.User?.FindFirst("tenant_id")?.Value;
        if (tenantClaim is null || !Guid.TryParse(tenantClaim, out var tenantId))
        {
            Context.Abort(); // no tenant scope — nothing to subscribe to
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(tenantId));
        await base.OnConnectedAsync();
    }

    internal static string GroupFor(Guid tenantId) => $"tenant:{tenantId}";
}

/// <summary>Pushes worklist-changed hints into the tenant's hub group.</summary>
internal sealed class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<WorklistHub> _hub;

    public SignalRRealtimeNotifier(IHubContext<WorklistHub> hub) => _hub = hub;

    public Task WorklistChangedAsync(Guid tenantId, string area, CancellationToken ct = default) =>
        _hub.Clients.Group(WorklistHub.GroupFor(tenantId))
            .SendAsync("worklistChanged", new { area }, ct);
}
