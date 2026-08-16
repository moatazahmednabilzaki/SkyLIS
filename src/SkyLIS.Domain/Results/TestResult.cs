using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Results;

public enum ResultStatus
{
    Entered = 0,
    TechnicallyValid = 1,
    MedicallyValid = 2,
    RerunOrdered = 3,
}

public enum ResultFlag
{
    Normal = 0,
    Low = 1,
    High = 2,
    CriticalLow = 3,
    CriticalHigh = 4,
}

/// <summary>
/// Tenant-owned aggregate: one entered result for one visit test line (M09).
/// Own consistency boundary so validation scales across many concurrent users.
/// State machine: Entered â†’ TechnicallyValid â†’ MedicallyValid (RerunOrdered voids).
/// SoD invariant: the enterer can never medically validate their own result.
/// </summary>
public sealed class TestResult : AggregateRoot, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VisitId { get; private set; }
    public Guid VisitTestId { get; private set; }
    public Guid PatientId { get; private set; }
    public string TestCode { get; private set; } = null!;
    public decimal Value { get; private set; }
    public string Unit { get; private set; } = null!;
    public ResultFlag Flag { get; private set; }
    public bool DeltaFlagged { get; private set; }
    public decimal? PreviousValue { get; private set; }
    public ResultStatus Status { get; private set; }

    public Guid EnteredByUserId { get; private set; }
    public DateTimeOffset EnteredAtUtc { get; private set; }
    public Guid? TechnicallyValidatedBy { get; private set; }
    public DateTimeOffset? TechnicallyValidatedAtUtc { get; private set; }
    public Guid? MedicallyValidatedBy { get; private set; }
    public DateTimeOffset? MedicallyValidatedAtUtc { get; private set; }
    public string? InterpretiveComment { get; private set; }
    public string? SignatureHash { get; private set; }
    public string? RerunReason { get; private set; }

    // P09.5 amendment trail: the pre-amendment value stays on the record forever.
    public bool IsAmended { get; private set; }
    public decimal? ValueBeforeAmendment { get; private set; }
    public string? AmendmentReason { get; private set; }
    public Guid? AmendedByUserId { get; private set; }
    public DateTimeOffset? AmendedAtUtc { get; private set; }

    public CriticalNotification? Critical { get; private set; }

    private TestResult() { } // EF

    public static TestResult Enter(
        Guid id, Guid tenantId, Guid visitId, Guid visitTestId, Guid patientId, string testCode,
        decimal value, string unit, ResultEvaluation evaluation, Guid enteredBy, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        if (enteredBy == Guid.Empty) throw new DomainException("The entering user must be identified (audit + SoD).");
        if (string.IsNullOrWhiteSpace(unit)) throw new DomainException("Result unit is required.");

        var result = new TestResult
        {
            Id = id,
            TenantId = tenantId,
            VisitId = visitId,
            VisitTestId = visitTestId,
            PatientId = patientId,
            TestCode = testCode,
            Value = value,
            Unit = unit.Trim(),
            Flag = evaluation.Flag,
            DeltaFlagged = evaluation.DeltaFlagged,
            PreviousValue = evaluation.PreviousValue,
            Status = ResultStatus.Entered,
            EnteredByUserId = enteredBy,
            EnteredAtUtc = nowUtc,
        };
        result.Raise(new ResultEntered(id, tenantId, visitId, visitTestId, testCode));

        if (evaluation.IsCritical)
        {
            result.Critical = CriticalNotification.Open(Guid.NewGuid(), tenantId, id, nowUtc);
            result.Raise(new CriticalValueFlagged(id, tenantId, visitId, testCode, value, unit));
        }

        // Auto-verification (P09.1): clean, in-range results skip manual technical review
        // when the test allows it. Critical or delta-flagged results never auto-verify.
        if (evaluation.AutoVerify && evaluation.Flag == ResultFlag.Normal && !evaluation.DeltaFlagged)
        {
            result.Status = ResultStatus.TechnicallyValid;
            result.TechnicallyValidatedBy = null; // system auto-verification
            result.TechnicallyValidatedAtUtc = nowUtc;
            result.Raise(new ResultTechnicallyValid(id, tenantId, visitId, visitTestId, AutoVerified: true));
        }

        return result;
    }

    public void AcceptTechnical(Guid supervisorId, DateTimeOffset nowUtc)
    {
        if (Status != ResultStatus.Entered)
            throw new InvalidStateTransitionException(nameof(TestResult), Status.ToString(), ResultStatus.TechnicallyValid.ToString());
        Status = ResultStatus.TechnicallyValid;
        TechnicallyValidatedBy = supervisorId;
        TechnicallyValidatedAtUtc = nowUtc;
        Raise(new ResultTechnicallyValid(Id, TenantId, VisitId, VisitTestId, AutoVerified: false));
    }

    public void OrderRerun(string reason)
    {
        if (Status is not (ResultStatus.Entered or ResultStatus.TechnicallyValid))
            throw new InvalidStateTransitionException(nameof(TestResult), Status.ToString(), ResultStatus.RerunOrdered.ToString());
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A rerun reason is mandatory.");
        Status = ResultStatus.RerunOrdered;
        RerunReason = reason.Trim();
        Raise(new ResultRerunOrdered(Id, TenantId, VisitId, VisitTestId, RerunReason));
    }

    /// <summary>
    /// Medical sign-out (P09.3). The signature binds signer, record version (xmin),
    /// timestamp, and a content hash computed by the caller over (value|unit|flag).
    /// </summary>
    public void ValidateMedical(Guid validatorId, string? interpretiveComment, string signatureHash, DateTimeOffset nowUtc)
    {
        if (Status != ResultStatus.TechnicallyValid)
            throw new InvalidStateTransitionException(nameof(TestResult), Status.ToString(), ResultStatus.MedicallyValid.ToString());
        if (validatorId == Guid.Empty) throw new DomainException("The validating user must be identified.");
        if (validatorId == EnteredByUserId)
            throw new DomainException("Segregation of duties: the user who entered a result cannot medically validate it.");
        if (string.IsNullOrWhiteSpace(signatureHash)) throw new DomainException("The e-signature content hash is required.");

        Status = ResultStatus.MedicallyValid;
        MedicallyValidatedBy = validatorId;
        MedicallyValidatedAtUtc = nowUtc;
        InterpretiveComment = string.IsNullOrWhiteSpace(interpretiveComment) ? null : interpretiveComment.Trim();
        SignatureHash = signatureHash;
        Raise(new ResultMedicallyValid(Id, TenantId, VisitId, VisitTestId));
    }

    /// <summary>
    /// P09.5: amend a MEDICALLY VALID result (pre-validation corrections go through the
    /// rerun flow instead). The old value is preserved, the amendment is re-signed, and a
    /// new critical cycle opens if the corrected value is critical. Reports rendered after
    /// this carry the AMENDED marking.
    /// </summary>
    public void Amend(
        decimal newValue, ResultEvaluation evaluation, string reason,
        Guid amendedBy, string signatureHash, DateTimeOffset nowUtc)
    {
        if (Status != ResultStatus.MedicallyValid)
            throw new InvalidStateTransitionException(nameof(TestResult), Status.ToString(), "Amended");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("An amendment reason is mandatory (P09.5).");
        if (amendedBy == Guid.Empty) throw new DomainException("The amending user must be identified.");
        if (amendedBy == EnteredByUserId)
            throw new DomainException("Segregation of duties: the user who entered a result cannot amend it.");
        if (string.IsNullOrWhiteSpace(signatureHash)) throw new DomainException("The e-signature content hash is required.");
        if (newValue == Value) throw new DomainException("The amended value must differ from the current value.");

        ValueBeforeAmendment = Value;
        Value = newValue;
        Flag = evaluation.Flag;
        DeltaFlagged = evaluation.DeltaFlagged;
        IsAmended = true;
        AmendmentReason = reason.Trim();
        AmendedByUserId = amendedBy;
        AmendedAtUtc = nowUtc;
        SignatureHash = signatureHash;
        Raise(new ResultAmended(Id, TenantId, VisitId, TestCode, ValueBeforeAmendment.Value, newValue));

        if (evaluation.IsCritical && (Critical is null || Critical.State == CriticalState.Closed))
        {
            Critical = CriticalNotification.Open(Guid.NewGuid(), TenantId, Id, nowUtc);
            Raise(new CriticalValueFlagged(Id, TenantId, VisitId, TestCode, newValue, Unit));
        }
    }

    public void DocumentCriticalCall(string calledPerson, string phone, bool readBackConfirmed, DateTimeOffset nowUtc)
    {
        if (Critical is null)
            throw new DomainException($"Result {TestCode} carries no critical value to document.");
        Critical.DocumentCall(calledPerson, phone, readBackConfirmed, nowUtc);
        if (Critical.State == CriticalState.Closed)
            Raise(new CriticalValueClosed(Id, TenantId, VisitId, TestCode));
    }
}

public enum CriticalState { Flagged = 0, ReadBackDocumented = 1, Closed = 2 }

/// <summary>
/// Critical (panic) value communication record (P09.4): every critical value must reach a
/// responsible caregiver and be documented with read-back confirmation. Audit-permanent.
/// </summary>
public sealed class CriticalNotification : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid TestResultId { get; private set; }
    public CriticalState State { get; private set; }
    public DateTimeOffset FlaggedAtUtc { get; private set; }
    public string? CalledPerson { get; private set; }
    public string? CalledPhone { get; private set; }
    public bool ReadBackConfirmed { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    private CriticalNotification() { } // EF

    internal static CriticalNotification Open(Guid id, Guid tenantId, Guid testResultId, DateTimeOffset nowUtc) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            TestResultId = testResultId,
            State = CriticalState.Flagged,
            FlaggedAtUtc = nowUtc,
        };

    internal void DocumentCall(string calledPerson, string phone, bool readBackConfirmed, DateTimeOffset nowUtc)
    {
        if (State == CriticalState.Closed)
            throw new DomainException("This critical value is already closed.");
        if (string.IsNullOrWhiteSpace(calledPerson) || string.IsNullOrWhiteSpace(phone))
            throw new DomainException("The called person and phone number are mandatory evidence.");
        CalledPerson = calledPerson.Trim();
        CalledPhone = phone.Trim();
        ReadBackConfirmed = readBackConfirmed;
        // Closure requires read-back (P09.4); a call without read-back stays open and re-escalates.
        State = readBackConfirmed ? CriticalState.Closed : CriticalState.ReadBackDocumented;
        if (readBackConfirmed) ClosedAtUtc = nowUtc;
    }
}

public sealed record ResultEntered(Guid ResultId, Guid TenantId, Guid VisitId, Guid VisitTestId, string TestCode) : DomainEvent, ITenantEvent;
public sealed record ResultTechnicallyValid(Guid ResultId, Guid TenantId, Guid VisitId, Guid VisitTestId, bool AutoVerified) : DomainEvent, ITenantEvent;
public sealed record ResultMedicallyValid(Guid ResultId, Guid TenantId, Guid VisitId, Guid VisitTestId) : DomainEvent, ITenantEvent;
public sealed record ResultRerunOrdered(Guid ResultId, Guid TenantId, Guid VisitId, Guid VisitTestId, string Reason) : DomainEvent, ITenantEvent;
public sealed record CriticalValueFlagged(Guid ResultId, Guid TenantId, Guid VisitId, string TestCode, decimal Value, string Unit) : DomainEvent, ITenantEvent;
public sealed record CriticalValueClosed(Guid ResultId, Guid TenantId, Guid VisitId, string TestCode) : DomainEvent, ITenantEvent;
public sealed record ResultAmended(Guid ResultId, Guid TenantId, Guid VisitId, string TestCode, decimal OldValue, decimal NewValue) : DomainEvent, ITenantEvent;
