namespace SkyLIS.Domain.Common;

/// <summary>Marker for domain events raised by aggregates and published via the outbox.</summary>
public interface IDomainEvent
{
    Guid EventId { get; }
}

/// <summary>Convenience base record for domain events.</summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}
