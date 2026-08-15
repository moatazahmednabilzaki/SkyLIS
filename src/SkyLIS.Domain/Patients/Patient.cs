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
}

public sealed record PatientRegistered(Guid PatientId, Guid TenantId, string PatientNumber) : DomainEvent;
