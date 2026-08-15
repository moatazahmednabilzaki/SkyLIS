using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Catalog;

public enum TestOrigin
{
    PlatformSeed = 0,
    PlatformPush = 1,
    Customized = 2,
    TenantDefined = 3,
}

public enum TestStatus { Draft = 0, InReview = 1, Approved = 2, Active = 3, Retired = 4, PendingActivation = 5 }

/// <summary>
/// Tenant-owned aggregate: one orderable test in this tenant's catalogue.
/// Platform-pushed tests (FR-MDM-071) arrive as PendingActivation and require a local
/// price to activate; tenant-defined tests exist in this tenant only.
/// SIMPLIFICATION (documented): price is held directly on the test as the walk-in price.
/// Full price-list versioning (P03.6) is a later slice; the invariant "no activation
/// without a resolvable price" is preserved.
/// </summary>
public sealed class LabTest : AggregateRoot, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Department { get; private set; } = null!;
    public Guid SampleTypeId { get; private set; }
    public Guid? RequiredConditionId { get; private set; }
    public Money? Price { get; private set; }
    public TestOrigin Origin { get; private set; }
    public TestStatus Status { get; private set; }

    private LabTest() { } // EF

    public static LabTest CreateTenantTest(
        Guid id, Guid tenantId, string code, string name, string department,
        Guid sampleTypeId, Guid? requiredConditionId, Money price)
    {
        var test = CreateCore(id, tenantId, code, name, department, sampleTypeId, requiredConditionId);
        test.Origin = TestOrigin.TenantDefined;
        test.Status = TestStatus.Draft;
        test.Price = price;
        return test;
    }

    public static LabTest CreateFromPlatformSeed(
        Guid id, Guid tenantId, string code, string name, string department,
        Guid sampleTypeId, Guid? requiredConditionId, Money price)
    {
        var test = CreateCore(id, tenantId, code, name, department, sampleTypeId, requiredConditionId);
        test.Origin = TestOrigin.PlatformSeed;
        test.Status = TestStatus.Active;
        test.Price = price;
        return test;
    }

    public static LabTest CreateFromPlatformPush(
        Guid id, Guid tenantId, string code, string name, string department,
        Guid sampleTypeId, Guid? requiredConditionId)
    {
        var test = CreateCore(id, tenantId, code, name, department, sampleTypeId, requiredConditionId);
        test.Origin = TestOrigin.PlatformPush;
        test.Status = TestStatus.PendingActivation; // never auto-activates: price required first
        return test;
    }

    private static LabTest CreateCore(
        Guid id, Guid tenantId, string code, string name, string department,
        Guid sampleTypeId, Guid? requiredConditionId)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Test code is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Test name is required.");
        if (string.IsNullOrWhiteSpace(department)) throw new DomainException("Department is required.");
        if (sampleTypeId == Guid.Empty) throw new DomainException("Sample type is required.");

        return new LabTest
        {
            Id = id,
            TenantId = tenantId,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Department = department.Trim(),
            SampleTypeId = sampleTypeId,
            RequiredConditionId = requiredConditionId,
        };
    }

    public void SubmitForReview()
    {
        if (Status != TestStatus.Draft)
            throw new InvalidStateTransitionException(nameof(LabTest), Status.ToString(), TestStatus.InReview.ToString());
        Status = TestStatus.InReview;
    }

    public void Approve()
    {
        if (Status != TestStatus.InReview)
            throw new InvalidStateTransitionException(nameof(LabTest), Status.ToString(), TestStatus.Approved.ToString());
        Status = TestStatus.Approved;
        Activate();
    }

    /// <summary>Activation gate: a test can never activate without a resolvable price.</summary>
    public void Activate()
    {
        if (Status is not (TestStatus.Approved or TestStatus.PendingActivation))
            throw new InvalidStateTransitionException(nameof(LabTest), Status.ToString(), TestStatus.Active.ToString());
        if (Price is null)
            throw new DomainException($"Test {Code} cannot activate without a price.");
        Status = TestStatus.Active;
    }

    /// <summary>Tenant activates a platform-pushed test by setting the local price.</summary>
    public void ActivatePushedTest(Money price)
    {
        if (Origin != TestOrigin.PlatformPush || Status != TestStatus.PendingActivation)
            throw new DomainException("Only a platform-pushed test pending activation can be activated this way.");
        Price = price;
        Status = TestStatus.Active;
    }

    /// <summary>Pushed tests cannot be deleted by tenants — retiring is the only removal path.</summary>
    public void Retire()
    {
        if (Status != TestStatus.Active)
            throw new InvalidStateTransitionException(nameof(LabTest), Status.ToString(), TestStatus.Retired.ToString());
        Status = TestStatus.Retired;
    }
}
