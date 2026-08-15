using Microsoft.Extensions.Logging;
using SkyLIS.Application.Common;
using SkyLIS.Application.Reports;
using SkyLIS.Domain.Reports;
using SkyLIS.Domain.Results;

namespace SkyLIS.Application.IntegrationHandlers;

/// <summary>
/// FR-SYS-011: every finalized report increments the tenant's monthly meter —
/// "one finalized report per visit" is the billing unit consumed by M01 (P01.3).
/// </summary>
internal sealed class ReportFinalizedMeteringHandler : IIntegrationEventHandler<ReportFinalized>
{
    private readonly IUsageMeterStore _meters;

    public ReportFinalizedMeteringHandler(IUsageMeterStore meters) => _meters = meters;

    public Task HandleAsync(ReportFinalized domainEvent, CancellationToken ct = default) =>
        _meters.IncrementFinalizedReportsAsync(domainEvent.TenantId, DateTimeOffset.UtcNow, ct);
}

/// <summary>
/// NFR-016: critical values trigger an immediate notification burst. Channels are behind
/// the INotificationSender port (dev sender logs; WhatsApp/SMS providers plug in later).
/// </summary>
internal sealed class CriticalValueNotificationHandler : IIntegrationEventHandler<CriticalValueFlagged>
{
    private readonly INotificationSender _sender;
    private readonly ILogger<CriticalValueNotificationHandler> _logger;

    public CriticalValueNotificationHandler(INotificationSender sender, ILogger<CriticalValueNotificationHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(CriticalValueFlagged domainEvent, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "CRITICAL VALUE burst: {TestCode} = {Value} {Unit} on visit {VisitId} (tenant {TenantId})",
            domainEvent.TestCode, domainEvent.Value, domainEvent.Unit, domainEvent.VisitId, domainEvent.TenantId);
        await _sender.SendAsync("portal", "bench+ordering-physician",
            $"CRITICAL: {domainEvent.TestCode} = {domainEvent.Value} {domainEvent.Unit}", ct);
    }
}
