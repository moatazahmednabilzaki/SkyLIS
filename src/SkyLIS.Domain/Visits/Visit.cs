using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Visits;

/// <summary>
/// Tenant-owned aggregate: one patient visit (the order). The commercial and clinical
/// anchor joining patient, tests, samples, results, report, and invoice (SRS Rev 2.0 M05).
/// Transaction boundary: the visit with its test lines and samples.
/// Concurrency: optimistic (xmin). Invariants: no visit without tests; every test line
/// bound to a sample (or awaiting recollection); guarded status transitions only.
/// </summary>
public sealed class Visit : AggregateRoot, ITenantOwned
{
    private readonly List<VisitTest> _tests = [];
    private readonly List<Sample> _samples = [];

    public Guid TenantId { get; private set; }
    public string VisitNumber { get; private set; } = null!;
    /// <summary>The branch this visit was registered at (P03.2) — drives per-branch numbering.</summary>
    public Guid BranchId { get; private set; }
    public Guid PatientId { get; private set; }
    public VisitStatus Status { get; private set; }
    public bool IsStat { get; private set; }
    public string? StatReason { get; private set; }
    public DateTimeOffset RegisteredAtUtc { get; private set; }

    public IReadOnlyCollection<VisitTest> Tests => _tests.AsReadOnly();
    public IReadOnlyCollection<Sample> Samples => _samples.AsReadOnly();

    private Visit() { } // EF

    public static Visit Register(
        Guid id, Guid tenantId, Guid branchId, string visitNumber, Guid patientId,
        IReadOnlyList<PlannedTest> plannedTests, IReadOnlyList<PlannedSample> plannedSamples,
        bool isStat, string? statReason, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        if (branchId == Guid.Empty) throw new DomainException("A visit shall be registered at a branch.");
        if (patientId == Guid.Empty) throw new DomainException("Patient id is required.");
        if (string.IsNullOrWhiteSpace(visitNumber)) throw new DomainException("Visit number is required.");
        if (plannedTests.Count == 0) throw new DomainException("A visit shall not be registered with zero tests.");
        if (plannedSamples.Count == 0) throw new DomainException("A visit requires at least one planned sample.");
        if (isStat && string.IsNullOrWhiteSpace(statReason))
            throw new DomainException("STAT priority requires a reason.");
        if (plannedTests.Any(t => t.Price is null))
            throw new DomainException("A visit shall not be registered with an unresolved price.");

        var visit = new Visit
        {
            Id = id,
            TenantId = tenantId,
            BranchId = branchId,
            VisitNumber = visitNumber,
            PatientId = patientId,
            Status = VisitStatus.Registered,
            IsStat = isStat,
            StatReason = isStat ? statReason!.Trim() : null,
            RegisteredAtUtc = nowUtc,
        };

        foreach (var ps in plannedSamples)
        {
            var sample = ps.DelayMinutes is null
                ? Sample.CreateReadyToCollect(ps.SampleId, tenantId, id, ps.Barcode, ps.SampleTypeId, ps.ConditionName)
                : Sample.CreateReserved(ps.SampleId, tenantId, id, ps.Barcode, ps.SampleTypeId, ps.ConditionName,
                    nowUtc.AddMinutes(ps.DelayMinutes.Value));
            visit._samples.Add(sample);
            if (sample.State == SampleState.ConditionPending)
                visit.Raise(new SampleReserved(id, sample.Id, tenantId, sample.ConditionReadyAtUtc!.Value));
        }

        foreach (var pt in plannedTests)
        {
            if (visit._samples.All(s => s.Id != pt.SampleId))
                throw new DomainException($"Test {pt.TestCode} references a sample outside this visit's plan.");
            visit._tests.Add(new VisitTest(pt.LineId, tenantId, pt.TestId, pt.TestCode, pt.SampleId, pt.Price!));
        }

        visit.Raise(new VisitRegistered(id, tenantId, patientId, visitNumber));
        return visit;
    }

    /// <summary>
    /// P05.4 add-on tests: extend an open visit with new tests on NEW samples (joining
    /// already-collected samples would break specimen integrity). Billing issues a
    /// supplementary invoice for the added amount.
    /// </summary>
    public void AddTests(
        IReadOnlyList<PlannedTest> plannedTests, IReadOnlyList<PlannedSample> plannedSamples, DateTimeOffset nowUtc)
    {
        EnsureNotTerminal();
        if (Status is VisitStatus.Validated or VisitStatus.Reported)
            throw new InvalidStateTransitionException(nameof(Visit), Status.ToString(), "add-on tests");
        if (plannedTests.Count == 0) throw new DomainException("Add at least one test.");
        if (plannedSamples.Count == 0) throw new DomainException("Add-on tests require at least one new sample.");
        if (plannedTests.Any(t => t.Price is null))
            throw new DomainException("A test shall not be added with an unresolved price.");
        var duplicate = plannedTests.FirstOrDefault(t => _tests.Any(existing =>
            existing.TestId == t.TestId && existing.Status != VisitTestStatus.Cancelled));
        if (duplicate is not null)
            throw new DomainException($"Test {duplicate.TestCode} is already on this visit.");

        foreach (var ps in plannedSamples)
        {
            var sample = ps.DelayMinutes is null
                ? Sample.CreateReadyToCollect(ps.SampleId, TenantId, Id, ps.Barcode, ps.SampleTypeId, ps.ConditionName)
                : Sample.CreateReserved(ps.SampleId, TenantId, Id, ps.Barcode, ps.SampleTypeId, ps.ConditionName,
                    nowUtc.AddMinutes(ps.DelayMinutes.Value));
            _samples.Add(sample);
            if (sample.State == SampleState.ConditionPending)
                Raise(new SampleReserved(Id, sample.Id, TenantId, sample.ConditionReadyAtUtc!.Value));
        }

        foreach (var pt in plannedTests)
        {
            if (_samples.All(s => s.Id != pt.SampleId))
                throw new DomainException($"Test {pt.TestCode} references a sample outside this visit's plan.");
            _tests.Add(new VisitTest(pt.LineId, TenantId, pt.TestId, pt.TestCode, pt.SampleId, pt.Price!));
        }

        Raise(new TestsAddedToVisit(Id, TenantId, plannedTests.Count));
    }

    public Money Total(string currency) =>
        _tests.Where(t => t.Status != VisitTestStatus.Cancelled)
              .Aggregate(Money.Zero(currency), (acc, t) => acc.Add(t.Price));

    public void CollectSample(Guid sampleId, DateTimeOffset nowUtc)
    {
        EnsureNotTerminal();
        var sample = FindSample(sampleId);
        sample.Collect(nowUtc);
        Raise(new SampleCollected(Id, sampleId, TenantId));
        foreach (var t in _tests.Where(t => t.SampleId == sampleId)) t.MarkPending();
        if (_samples.All(s => s.State is SampleState.Collected or SampleState.Received or SampleState.InProcess
                                        or SampleState.Completed or SampleState.Rejected)
            && Status == VisitStatus.Registered)
        {
            Status = VisitStatus.Collected;
        }
    }

    public void ReceiveSample(Guid sampleId, DateTimeOffset nowUtc)
    {
        EnsureNotTerminal();
        var sample = FindSample(sampleId);
        sample.Receive(nowUtc);
        Raise(new SampleReceived(Id, sampleId, TenantId));
        if (Status is VisitStatus.Registered or VisitStatus.Collected)
            Status = VisitStatus.Received;
    }

    /// <summary>Rejection cycle (P07.3): tests revert to AwaitingSample; a recollection sample is spawned.</summary>
    public Sample RejectSample(Guid sampleId, string reasonCode, Guid recollectionSampleId, string recollectionBarcode, DateTimeOffset nowUtc)
    {
        EnsureNotTerminal();
        if (string.IsNullOrWhiteSpace(reasonCode)) throw new DomainException("A coded rejection reason is mandatory.");
        var sample = FindSample(sampleId);
        sample.Reject(reasonCode);
        Raise(new SampleRejected(Id, sampleId, TenantId, reasonCode));

        var recollection = sample.ConditionDelayed
            ? Sample.CreateReserved(recollectionSampleId, TenantId, Id, recollectionBarcode, sample.SampleTypeId,
                sample.ConditionName, nowUtc)
            : Sample.CreateReadyToCollect(recollectionSampleId, TenantId, Id, recollectionBarcode, sample.SampleTypeId,
                sample.ConditionName);
        _samples.Add(recollection);

        foreach (var t in _tests.Where(t => t.SampleId == sampleId && t.Status != VisitTestStatus.Cancelled))
            t.Rebind(recollection.Id);
        return recollection;
    }

    /// <summary>Result entered for a line (M09): requires the sample to be received in the lab.</summary>
    public void MarkTestEntered(Guid visitTestId)
    {
        EnsureNotTerminal();
        var line = FindLine(visitTestId);
        // Sample-state check first: "sample not received" is the actionable message for
        // any pre-entry line state (AwaitingSample included).
        var sample = FindSample(line.SampleId);
        if (sample.State != SampleState.Received)
            throw new DomainException($"Sample {sample.Barcode} must be received at accessioning before results can be entered.");
        if (line.Status is not (VisitTestStatus.Pending or VisitTestStatus.InProcess))
            throw new InvalidStateTransitionException(nameof(VisitTest), line.Status.ToString(), VisitTestStatus.Entered.ToString());
        line.SetStatus(VisitTestStatus.Entered);
        if (Status is VisitStatus.Received or VisitStatus.Collected or VisitStatus.Registered)
            Status = VisitStatus.InProcess;
    }

    public void MarkTestTechnicallyValid(Guid visitTestId)
    {
        EnsureNotTerminal();
        var line = FindLine(visitTestId);
        if (line.Status != VisitTestStatus.Entered)
            throw new InvalidStateTransitionException(nameof(VisitTest), line.Status.ToString(), VisitTestStatus.TechValid.ToString());
        line.SetStatus(VisitTestStatus.TechValid);
    }

    public void MarkTestMedicallyValid(Guid visitTestId)
    {
        EnsureNotTerminal();
        var line = FindLine(visitTestId);
        if (line.Status != VisitTestStatus.TechValid)
            throw new InvalidStateTransitionException(nameof(VisitTest), line.Status.ToString(), VisitTestStatus.MedValid.ToString());
        line.SetStatus(VisitTestStatus.MedValid);
        if (_tests.Where(t => t.Status != VisitTestStatus.Cancelled).All(t => t.Status == VisitTestStatus.MedValid))
            Status = VisitStatus.Validated;
    }

    /// <summary>Final report rendered (M10): the visit reaches Reported.</summary>
    public void MarkReported()
    {
        if (Status != VisitStatus.Validated)
            throw new InvalidStateTransitionException(nameof(Visit), Status.ToString(), VisitStatus.Reported.ToString());
        Status = VisitStatus.Reported;
        foreach (var line in _tests.Where(t => t.Status == VisitTestStatus.MedValid))
            line.SetStatus(VisitTestStatus.Reported);
    }

    /// <summary>Rerun ordered: the line returns to Pending for a fresh entry.</summary>
    public void MarkTestRerun(Guid visitTestId)
    {
        EnsureNotTerminal();
        var line = FindLine(visitTestId);
        if (line.Status is not (VisitTestStatus.Entered or VisitTestStatus.TechValid))
            throw new InvalidStateTransitionException(nameof(VisitTest), line.Status.ToString(), VisitTestStatus.Pending.ToString());
        line.SetStatus(VisitTestStatus.Pending);
    }

    private VisitTest FindLine(Guid visitTestId) =>
        _tests.FirstOrDefault(t => t.Id == visitTestId)
        ?? throw new DomainException($"Test line {visitTestId} does not belong to visit {VisitNumber}.");

    /// <summary>P07.3: reception documents that the patient was informed of the rejection.</summary>
    public void MarkPatientInformed(Guid sampleId, DateTimeOffset nowUtc)
    {
        EnsureNotTerminal();
        var sample = FindSample(sampleId);
        sample.MarkPatientInformed(nowUtc);
        Raise(new PatientInformedOfRejection(Id, sampleId, TenantId));
    }

    public void Cancel(string reason)
    {
        if (Status >= VisitStatus.Reported)
            throw new InvalidStateTransitionException(nameof(Visit), Status.ToString(), VisitStatus.Cancelled.ToString());
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A cancellation reason is mandatory.");
        Status = VisitStatus.Cancelled;
        foreach (var t in _tests) t.Cancel();
        Raise(new VisitCancelled(Id, TenantId, reason.Trim()));
    }

    private Sample FindSample(Guid sampleId) =>
        _samples.FirstOrDefault(s => s.Id == sampleId)
        ?? throw new DomainException($"Sample {sampleId} does not belong to visit {VisitNumber}.");

    private void EnsureNotTerminal()
    {
        if (Status is VisitStatus.Cancelled or VisitStatus.Closed)
            throw new DomainException($"Visit {VisitNumber} is {Status} and cannot be modified.");
    }
}

/// <summary>Input to Visit.Register produced by the SpecimenPlanner domain service.</summary>
public sealed record PlannedTest(Guid LineId, Guid TestId, string TestCode, Guid SampleId, Money? Price);
public sealed record PlannedSample(Guid SampleId, string Barcode, Guid SampleTypeId, string? ConditionName, int? DelayMinutes);
