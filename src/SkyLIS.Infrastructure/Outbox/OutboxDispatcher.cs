using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Common;
using SkyLIS.Infrastructure.Persistence;
using SkyLIS.Infrastructure.Tenancy;

namespace SkyLIS.Infrastructure.Outbox;

/// <summary>
/// Publishes transactional outbox messages to their integration handlers.
/// Guarantees: at-least-once delivery (claim via FOR UPDATE SKIP LOCKED — safe across
/// instances), idempotent consumption (inbox rows written atomically with handler
/// effects), bounded retries with poison capture (attempts + last_error), and per-message
/// tenant context so handler queries satisfy RLS. The in-process handler invocation is
/// the seam where MassTransit/RabbitMQ publication plugs in (ADR: event-driven
/// integration) without touching producers.
/// </summary>
public sealed class OutboxDispatcher : BackgroundService
{
    public const int MaxAttempts = 5;
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox dispatcher started (poll {Seconds}s, max {MaxAttempts} attempts)",
            PollDelay.TotalSeconds, MaxAttempts);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchNextAsync(stoppingToken);
                if (!dispatched)
                    await Task.Delay(PollDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatcher loop failure; backing off");
                await Task.Delay(PollDelay, stoppingToken);
            }
        }
    }

    /// <summary>Claims and processes one message; returns false when the queue is drained.</summary>
    private async Task<bool> DispatchNextAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SkyLisDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var messageId = await db.Database
            .SqlQuery<Guid>($@"
                SELECT id AS ""Value"" FROM outbox.outbox_messages
                WHERE processed_at_utc IS NULL AND attempts < {MaxAttempts}
                ORDER BY occurred_at_utc, id
                LIMIT 1
                FOR UPDATE SKIP LOCKED")
            .FirstOrDefaultAsync(ct);
        if (messageId == Guid.Empty)
            return false;

        var message = await db.OutboxMessages.FirstAsync(m => m.Id == messageId, ct);
        try
        {
            // Handler effects must run under the message's tenant (EF filters + RLS).
            if (message.TenantId is not null)
            {
                tenantContext.Set(message.TenantId.Value);
                await db.Database.ExecuteSqlAsync(
                    $"SELECT set_config('app.tenant_id', {message.TenantId.Value.ToString()}, true)", ct);
            }

            var handled = await InvokeHandlersAsync(scope.ServiceProvider, db, message, ct);
            message.MarkProcessed(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct); // handler effects + inbox rows + processed flag: atomic
            await transaction.CommitAsync(ct);

            if (handled > 0)
                _logger.LogInformation("Outbox {EventType} {EventId} dispatched to {Handlers} handler(s)",
                    message.EventType, message.Id, handled);
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            await RecordFailureAsync(message.Id, ex, ct);
            return true; // keep draining; the failed message retries after its attempt count grows
        }
    }

    private static async Task<int> InvokeHandlersAsync(
        IServiceProvider services, SkyLisDbContext db, OutboxMessage message, CancellationToken ct)
    {
        var eventType = typeof(IDomainEvent).Assembly.GetType(message.EventType)
            ?? throw new InvalidOperationException($"Unknown event type '{message.EventType}'.");
        var domainEvent = (IDomainEvent)(JsonSerializer.Deserialize(message.Payload, eventType)
            ?? throw new InvalidOperationException($"Payload of {message.Id} deserialized to null."));

        var handlerInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var invoked = 0;
        foreach (var handler in services.GetServices(handlerInterface))
        {
            var handlerName = handler!.GetType().FullName!;
            var alreadyConsumed = await db.InboxConsumptions
                .AnyAsync(c => c.HandlerName == handlerName && c.EventId == domainEvent.EventId, ct);
            if (alreadyConsumed)
                continue; // idempotent consumption: this handler already saw this event

            var method = handlerInterface.GetMethod(nameof(IIntegrationEventHandler<IDomainEvent>.HandleAsync))!;
            await (Task)method.Invoke(handler, [domainEvent, ct])!;

            db.InboxConsumptions.Add(new InboxConsumption
            {
                HandlerName = handlerName,
                EventId = domainEvent.EventId,
                ProcessedAtUtc = DateTimeOffset.UtcNow,
            });
            invoked++;
        }
        return invoked;
    }

    /// <summary>The failing transaction rolled back, so the attempt count is persisted separately.</summary>
    private async Task RecordFailureAsync(Guid messageId, Exception exception, CancellationToken ct)
    {
        _logger.LogError(exception, "Outbox message {MessageId} failed", messageId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SkyLisDbContext>();
        var error = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message;
        await db.Database.ExecuteSqlAsync($@"
            UPDATE outbox.outbox_messages
            SET attempts = attempts + 1, last_error = {error}
            WHERE id = {messageId}", ct);
    }
}

/// <summary>Inbox row: proof that one handler consumed one event (deduplication).</summary>
public sealed class InboxConsumption
{
    public string HandlerName { get; init; } = null!;
    public Guid EventId { get; init; }
    public DateTimeOffset ProcessedAtUtc { get; init; }
}

/// <summary>Platform ops view over the outbox (FR-SYS-010 monitored background processing).</summary>
internal sealed class OutboxStatusQueries : Application.Platform.IOutboxStatusQueries
{
    private readonly SkyLisDbContext _db;
    public OutboxStatusQueries(SkyLisDbContext db) => _db = db;

    public async Task<Application.Platform.OutboxStatusDto> StatusAsync(CancellationToken ct = default)
    {
        var pending = await _db.OutboxMessages.AsNoTracking()
            .CountAsync(m => m.ProcessedAtUtc == null && m.Attempts < OutboxDispatcher.MaxAttempts, ct);
        var processed = await _db.OutboxMessages.AsNoTracking()
            .CountAsync(m => m.ProcessedAtUtc != null, ct);
        var poisoned = await _db.OutboxMessages.AsNoTracking()
            .CountAsync(m => m.ProcessedAtUtc == null && m.Attempts >= OutboxDispatcher.MaxAttempts, ct);
        var failures = await _db.OutboxMessages.AsNoTracking()
            .Where(m => m.LastError != null)
            .OrderByDescending(m => m.OccurredAtUtc)
            .Take(10)
            .Select(m => new Application.Platform.OutboxFailureDto(
                m.Id, m.EventType, m.Attempts, m.LastError, m.OccurredAtUtc))
            .ToListAsync(ct);
        return new Application.Platform.OutboxStatusDto(pending, processed, poisoned, failures);
    }
}
