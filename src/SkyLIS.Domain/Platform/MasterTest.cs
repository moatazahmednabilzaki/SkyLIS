using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Platform;

/// <summary>
/// Platform-owned aggregate (P01.7): one test in the platform master catalogue. Pushing
/// (FR-MDM-071) raises one tenant event per target tenant; the outbox consumer creates
/// the test there as PendingActivation — tenants must set a local price to activate.
/// </summary>
public sealed class MasterTest : AggregateRoot
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Department { get; private set; } = null!;
    /// <summary>Resolved per tenant by name at push time (seeded by the country pack).</summary>
    public string SampleTypeName { get; private set; } = null!;
    public string ContainerName { get; private set; } = null!;
    public string? ConditionName { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? LastPushedAtUtc { get; private set; }
    public int PushCount { get; private set; }

    private MasterTest() { } // EF

    public static MasterTest Create(
        Guid id, string code, string name, string department,
        string sampleTypeName, string containerName, string? conditionName, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Master test code is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Master test name is required.");
        if (string.IsNullOrWhiteSpace(department)) throw new DomainException("Department is required.");
        if (string.IsNullOrWhiteSpace(sampleTypeName)) throw new DomainException("Sample type name is required.");
        if (string.IsNullOrWhiteSpace(containerName)) throw new DomainException("Container name is required.");
        return new MasterTest
        {
            Id = id,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Department = department.Trim(),
            SampleTypeName = sampleTypeName.Trim(),
            ContainerName = containerName.Trim(),
            ConditionName = string.IsNullOrWhiteSpace(conditionName) ? null : conditionName.Trim(),
            CreatedAtUtc = nowUtc,
        };
    }

    /// <summary>FR-MDM-071: one reliable event per target tenant — the outbox fans out.</summary>
    public void PushTo(IReadOnlyCollection<Guid> tenantIds, DateTimeOffset nowUtc)
    {
        if (tenantIds.Count == 0)
            throw new DomainException("There are no active tenants to push to.");
        foreach (var tenantId in tenantIds)
            Raise(new MasterTestPushed(Id, tenantId, Code, Name, Department, SampleTypeName, ContainerName, ConditionName));
        LastPushedAtUtc = nowUtc;
        PushCount += tenantIds.Count;
    }
}

public sealed record MasterTestPushed(
    Guid MasterTestId, Guid TenantId, string Code, string Name, string Department,
    string SampleTypeName, string ContainerName, string? ConditionName) : DomainEvent, ITenantEvent;
