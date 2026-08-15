using System.Text.Json;
using SkyLIS.Domain.Common;

namespace SkyLIS.Infrastructure.Outbox;

/// <summary>
/// Transactional outbox row: written atomically with the business change, published by a
/// background dispatcher (at-least-once) with inbox/deduplication on consumers.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public Guid? TenantId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage() { } // EF

    public static OutboxMessage From(IDomainEvent domainEvent, Guid? tenantId) => new()
    {
        Id = domainEvent.EventId,
        TenantId = tenantId,
        EventType = domainEvent.GetType().FullName!,
        Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
        OccurredAtUtc = DateTimeOffset.UtcNow,
    };

    public void MarkProcessed(DateTimeOffset nowUtc) => ProcessedAtUtc = nowUtc;

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
    }
}
