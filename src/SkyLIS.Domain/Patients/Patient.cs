using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Patients;

public enum Sex { Female = 0, Male = 1 }

/// <summary>
/// Tenant-owned aggregate: the patient master record. Created automatically behind
/// visit registration (SRS Rev 2.0 M04): captured once, reused on every later visit.
/// Search keys: mobile number, partial name, national ID, patient number.
/// </summary>
public sealed class Patient : AggregateRoot, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string PatientNumber { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public Sex Sex { get; private set; }
    public DateOnly DateOfBirth { get; private set; }
    public PhoneNumber Mobile { get; private set; } = null!;
    public string? NationalId { get; private set; }
    public bool IsConfidential { get; private set; }
    public DateTimeOffset RegisteredAtUtc { get; private set; }
    public DateTimeOffset? LastVisitAtUtc { get; private set; }
    /// <summary>P04.4: set when this record was merged into a survivor; hidden from search.</summary>
    public Guid? MergedIntoPatientId { get; private set; }
    /// <summary>P04.5: identity anonymized per data-subject request; clinical data retained.</summary>
    public bool IsErased { get; private set; }

    private Patient() { } // EF

    public static Patient Register(
        Guid id, Guid tenantId, string patientNumber, string fullName, Sex sex,
        DateOnly dateOfBirth, PhoneNumber mobile, string? nationalId, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        if (string.IsNullOrWhiteSpace(patientNumber)) throw new DomainException("Patient number is required.");
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 3)
            throw new DomainException("Full name of at least 3 characters is required.");
        if (dateOfBirth > DateOnly.FromDateTime(nowUtc.UtcDateTime))
            throw new DomainException("Date of birth cannot be in the future.");

        var patient = new Patient
        {
            Id = id,
            TenantId = tenantId,
            PatientNumber = patientNumber,
            FullName = fullName.Trim(),
            Sex = sex,
            DateOfBirth = dateOfBirth,
            Mobile = mobile,
            NationalId = string.IsNullOrWhiteSpace(nationalId) ? null : nationalId.Trim(),
            RegisteredAtUtc = nowUtc,
        };
        patient.Raise(new PatientRegistered(id, tenantId, patientNumber));
        return patient;
    }

    /// <summary>Age in whole years — part of the identity-confirmation triple (last visit, age, gender).</summary>
    public int AgeInYears(DateOnly today)
    {
        var age = today.Year - DateOfBirth.Year;
        if (today < DateOfBirth.AddYears(age)) age--;
        return age;
    }

    public void UpdateDemographics(string fullName, PhoneNumber mobile, string? nationalId)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 3)
            throw new DomainException("Full name of at least 3 characters is required.");
        FullName = fullName.Trim();
        Mobile = mobile;
        NationalId = string.IsNullOrWhiteSpace(nationalId) ? null : nationalId.Trim();
    }

    public void RecordVisit(DateTimeOffset visitAtUtc) => LastVisitAtUtc = visitAtUtc;

    public void MarkConfidential() => IsConfidential = true;

    /// <summary>
    /// P04.4 duplicate merge: this record loses; its clinical artifacts are re-pointed to
    /// the survivor by the merge service. The row stays for the audit trail.
    /// </summary>
    public void MarkMergedInto(Guid survivorPatientId)
    {
        if (survivorPatientId == Guid.Empty || survivorPatientId == Id)
            throw new DomainException("A patient cannot be merged into itself.");
        if (MergedIntoPatientId is not null)
            throw new DomainException($"Patient {PatientNumber} was already merged.");
        if (IsErased) throw new DomainException("An erased record cannot be merged.");
        MergedIntoPatientId = survivorPatientId;
    }

    /// <summary>
    /// P04.5 erasure: identity fields are anonymized; clinical records are RETAINED under
    /// laboratory record-keeping obligations — that retention is the documented lawful basis.
    /// </summary>
    public void Anonymize()
    {
        if (IsErased) throw new DomainException($"Patient {PatientNumber} is already erased.");
        FullName = $"ERASED {PatientNumber}";
        Mobile = PhoneNumber.Of("+200000000000");
        NationalId = null;
        IsErased = true;
    }
}

public enum DataSubjectRequestKind { Export = 0, Erasure = 1 }
public enum DataSubjectRequestStatus { Completed = 0, PendingApproval = 1, Approved = 2, Rejected = 3 }

/// <summary>
/// Tenant-owned aggregate (P04.5): one data-subject request. Exports complete immediately
/// (and leave this audited trace); erasure needs explicit approval (SoD) and is blocked
/// while the patient has open clinical work.
/// </summary>
public sealed class DataSubjectRequest : AggregateRoot, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid PatientId { get; private set; }
    public DataSubjectRequestKind Kind { get; private set; }
    public DataSubjectRequestStatus Status { get; private set; }
    public string Reason { get; private set; } = null!;
    public Guid? RequestedByUserId { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    private DataSubjectRequest() { } // EF

    public static DataSubjectRequest Create(
        Guid id, Guid tenantId, Guid patientId, DataSubjectRequestKind kind,
        string reason, Guid? requestedBy, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        if (patientId == Guid.Empty) throw new DomainException("Patient id is required.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A request reason is mandatory (P04.5).");
        return new DataSubjectRequest
        {
            Id = id,
            TenantId = tenantId,
            PatientId = patientId,
            Kind = kind,
            Status = kind == DataSubjectRequestKind.Export
                ? DataSubjectRequestStatus.Completed
                : DataSubjectRequestStatus.PendingApproval,
            Reason = reason.Trim(),
            RequestedByUserId = requestedBy,
            CreatedAtUtc = nowUtc,
        };
    }

    public void Approve(Guid? decidedBy, DateTimeOffset nowUtc)
    {
        if (Status != DataSubjectRequestStatus.PendingApproval)
            throw new InvalidStateTransitionException(nameof(DataSubjectRequest), Status.ToString(), "Approved");
        Status = DataSubjectRequestStatus.Approved;
        DecidedByUserId = decidedBy;
        DecidedAtUtc = nowUtc;
    }

    public void Reject(Guid? decidedBy, DateTimeOffset nowUtc)
    {
        if (Status != DataSubjectRequestStatus.PendingApproval)
            throw new InvalidStateTransitionException(nameof(DataSubjectRequest), Status.ToString(), "Rejected");
        Status = DataSubjectRequestStatus.Rejected;
        DecidedByUserId = decidedBy;
        DecidedAtUtc = nowUtc;
    }
}

public sealed record PatientRegistered(Guid PatientId, Guid TenantId, string PatientNumber) : DomainEvent;
