namespace SkyLIS.Domain.Common;

/// <summary>Marker for domain events raised by aggregates and published via the outbox.</summary>
public interface IDomainEvent
{
    Guid EventId { get; }
}

/// <summary>A domain event owned by one tenant — consumers may fan out tenant-scoped.</summary>
public interface ITenantEvent : IDomainEvent
{
    Guid TenantId { get; }
}

/// <summary>Convenience base record for domain events.</summary>
public abstract record DomainEvent : IDomainEvent
{
    /// <summary>init (not get-only) so the id round-trips through outbox serialization —
    /// consumers deduplicate on it (inbox pattern).</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();
}
