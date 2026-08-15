namespace SkyLIS.Domain.Common;

/// <summary>Thrown when a business invariant is violated. Mapped to HTTP 422 by the API layer.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

/// <summary>Thrown on an illegal state-machine transition. Mapped to HTTP 409 by the API layer.</summary>
public sealed class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(string entity, string from, string to)
        : base($"{entity} cannot transition from {from} to {to}.") { }
}
