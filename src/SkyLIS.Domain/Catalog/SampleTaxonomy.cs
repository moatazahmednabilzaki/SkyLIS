using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Catalog;

/// <summary>Tenant-owned specimen taxonomy: sample type → conditions (SRS Rev 2.0 P03.4).</summary>
public sealed class SampleType : AggregateRoot, ITenantOwned
{
    private readonly List<SampleCondition> _conditions = [];

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string ContainerName { get; private set; } = null!;
    public IReadOnlyCollection<SampleCondition> Conditions => _conditions.AsReadOnly();

    private SampleType() { } // EF

    public static SampleType Create(Guid id, Guid tenantId, string name, string containerName)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Sample type name is required.");
        if (string.IsNullOrWhiteSpace(containerName)) throw new DomainException("Container name is required.");
        return new SampleType { Id = id, TenantId = tenantId, Name = name.Trim(), ContainerName = containerName.Trim() };
    }

    public SampleCondition AddCondition(Guid conditionId, string name, int? delayMinutes, string compatibilityGroup)
    {
        if (_conditions.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException($"Condition '{name}' already exists on sample type {Name}.");
        var condition = new SampleCondition(conditionId, TenantId, Id, name, delayMinutes, compatibilityGroup);
        _conditions.Add(condition);
        return condition;
    }
}

/// <summary>
/// A pre-analytic condition (e.g., Fasting 8h, Post-prandial +2h). Conditions sharing a
/// CompatibilityGroup consolidate onto ONE printed Sample ID; different groups force
/// separate samples (and the reservation flow when DelayMinutes is set).
/// </summary>
public sealed class SampleCondition : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid SampleTypeId { get; private set; }
    public string Name { get; private set; } = null!;
    /// <summary>Minutes to wait from the trigger (e.g., meal) before collection; null = collect now.</summary>
    public int? DelayMinutes { get; private set; }
    public string CompatibilityGroup { get; private set; } = null!;

    private SampleCondition() { } // EF

    internal SampleCondition(Guid id, Guid tenantId, Guid sampleTypeId, string name, int? delayMinutes, string compatibilityGroup)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Condition name is required.");
        if (delayMinutes is < 0 or > 24 * 60) throw new DomainException("Condition delay must be between 0 minutes and 24 hours.");
        if (string.IsNullOrWhiteSpace(compatibilityGroup)) throw new DomainException("Compatibility group is required.");
        TenantId = tenantId;
        SampleTypeId = sampleTypeId;
        Name = name.Trim();
        DelayMinutes = delayMinutes;
        CompatibilityGroup = compatibilityGroup.Trim().ToUpperInvariant();
    }
}
