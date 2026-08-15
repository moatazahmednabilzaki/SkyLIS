using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Visits;

/// <summary>One ordered test line on a visit, bound to a sample, priced at registration time.</summary>
public sealed class VisitTest : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid TestId { get; private set; }
    public string TestCode { get; private set; } = null!;
    public Guid SampleId { get; private set; }
    public Money Price { get; private set; } = null!;
    public VisitTestStatus Status { get; private set; }

    private VisitTest() { } // EF

    internal VisitTest(Guid id, Guid tenantId, Guid testId, string testCode, Guid sampleId, Money price)
        : base(id)
    {
        TenantId = tenantId;
        TestId = testId;
        TestCode = testCode;
        SampleId = sampleId;
        Price = price;
        Status = VisitTestStatus.AwaitingSample;
    }

    internal void MarkPending()
    {
        if (Status == VisitTestStatus.AwaitingSample) Status = VisitTestStatus.Pending;
    }

    internal void Rebind(Guid newSampleId)
    {
        SampleId = newSampleId;
        Status = VisitTestStatus.AwaitingSample;
    }

    internal void Cancel() => Status = VisitTestStatus.Cancelled;
}
