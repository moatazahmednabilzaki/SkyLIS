using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Visits;

/// <summary>
/// Sample within a visit. State machine (SRS Rev 2.0, Appendix A):
/// Reserved → ConditionPending → ReadyToCollect → Collected → Received → InProcess → Completed,
/// with a Rejected branch at accessioning. Ready-now samples skip the reservation states.
/// </summary>
public sealed class Sample : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VisitId { get; private set; }
    public string Barcode { get; private set; } = null!;
    public Guid SampleTypeId { get; private set; }
    public string? ConditionName { get; private set; }
    public bool ConditionDelayed { get; private set; }
    public SampleState State { get; private set; }
    public DateTimeOffset? ConditionReadyAtUtc { get; private set; }
    public DateTimeOffset? CollectedAtUtc { get; private set; }
    public DateTimeOffset? ReceivedAtUtc { get; private set; }
    public string? RejectionReasonCode { get; private set; }

    private Sample() { } // EF

    private Sample(Guid id, Guid tenantId, Guid visitId, string barcode, Guid sampleTypeId, string? conditionName)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(barcode)) throw new DomainException("Sample barcode is required.");
        TenantId = tenantId;
        VisitId = visitId;
        Barcode = barcode;
        SampleTypeId = sampleTypeId;
        ConditionName = conditionName;
    }

    internal static Sample CreateReadyToCollect(Guid id, Guid tenantId, Guid visitId, string barcode, Guid sampleTypeId, string? conditionName) =>
        new(id, tenantId, visitId, barcode, sampleTypeId, conditionName) { State = SampleState.ReadyToCollect };

    internal static Sample CreateReserved(Guid id, Guid tenantId, Guid visitId, string barcode, Guid sampleTypeId, string? conditionName, DateTimeOffset readyAtUtc) =>
        new(id, tenantId, visitId, barcode, sampleTypeId, conditionName)
        {
            State = SampleState.ConditionPending,
            ConditionDelayed = true,
            ConditionReadyAtUtc = readyAtUtc,
        };

    /// <summary>The condition window opened — the sample surfaces on the Phlebotomist worklist.</summary>
    public void MarkReadyToCollect(DateTimeOffset nowUtc)
    {
        if (State != SampleState.ConditionPending)
            throw new InvalidStateTransitionException(nameof(Sample), State.ToString(), SampleState.ReadyToCollect.ToString());
        if (ConditionReadyAtUtc is not null && nowUtc < ConditionReadyAtUtc)
            throw new DomainException($"Sample {Barcode} condition window opens at {ConditionReadyAtUtc:HH:mm} UTC.");
        State = SampleState.ReadyToCollect;
    }

    internal void Collect(DateTimeOffset nowUtc)
    {
        if (State == SampleState.ConditionPending && ConditionReadyAtUtc is not null && nowUtc >= ConditionReadyAtUtc)
            State = SampleState.ReadyToCollect; // window opened; allow collect in one step
        if (State != SampleState.ReadyToCollect)
            throw new InvalidStateTransitionException(nameof(Sample), State.ToString(), SampleState.Collected.ToString());
        State = SampleState.Collected;
        CollectedAtUtc = nowUtc;
    }

    internal void Receive(DateTimeOffset nowUtc)
    {
        if (State != SampleState.Collected)
            throw new InvalidStateTransitionException(nameof(Sample), State.ToString(), SampleState.Received.ToString());
        State = SampleState.Received;
        ReceivedAtUtc = nowUtc;
    }

    internal void Reject(string reasonCode)
    {
        // In-process samples cannot be rejected (P07.3); only pre-processing states can.
        if (State is not (SampleState.Collected or SampleState.Received))
            throw new DomainException($"Sample {Barcode} in state {State} cannot be rejected.");
        State = SampleState.Rejected;
        RejectionReasonCode = reasonCode;
    }
}
