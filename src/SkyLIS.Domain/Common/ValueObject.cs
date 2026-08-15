namespace SkyLIS.Domain.Common;

/// <summary>Base class for value objects: equality by components, immutable by convention.</summary>
public abstract class ValueObject
{
    protected abstract IEnumerable<object?> EqualityComponents();

    public override bool Equals(object? obj) =>
        obj is ValueObject other && GetType() == other.GetType() &&
        EqualityComponents().SequenceEqual(other.EqualityComponents());

    public override int GetHashCode() =>
        EqualityComponents().Aggregate(GetType().GetHashCode(), (h, c) => HashCode.Combine(h, c));
}
