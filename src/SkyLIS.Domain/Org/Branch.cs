using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Org;

/// <summary>
/// Tenant-owned aggregate: a physical lab location (SRS Rev 2.0 P03.2). Every visit and
/// invoice is registered AT a branch; human-facing number series run per branch. The MAIN
/// branch is created automatically on tenant provisioning and can never be deactivated.
/// </summary>
public sealed class Branch : AggregateRoot, ITenantOwned
{
    private readonly List<Department> _departments = [];

    public Guid TenantId { get; private set; }
    /// <summary>Short uppercase code embedded in visit/invoice numbers (e.g. V-MAIN-260815-0001).</summary>
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public bool IsMain { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<Department> Departments => _departments.AsReadOnly();

    private Branch() { } // EF

    public static Branch Create(
        Guid id, Guid tenantId, string code, string name,
        string? address, string? phone, bool isMain, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        var normalized = code?.Trim().ToUpperInvariant() ?? "";
        if (normalized.Length is < 2 or > 10 || !normalized.All(char.IsLetterOrDigit))
            throw new DomainException("Branch code must be 2–10 letters or digits.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Branch name is required.");

        var branch = new Branch
        {
            Id = id,
            TenantId = tenantId,
            Code = normalized,
            Name = name.Trim(),
            Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            IsMain = isMain,
            IsActive = true,
            CreatedAtUtc = nowUtc,
        };
        branch.Raise(new BranchCreated(id, tenantId, normalized));
        return branch;
    }

    public Department AddDepartment(Guid departmentId, string code, string name)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? "";
        if (normalized.Length is < 2 or > 10 || !normalized.All(char.IsLetterOrDigit))
            throw new DomainException("Department code must be 2–10 letters or digits.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Department name is required.");
        if (_departments.Any(d => d.Code == normalized))
            throw new DomainException($"Department '{normalized}' already exists on branch {Code}.");

        var department = new Department(departmentId, TenantId, Id, normalized, name);
        _departments.Add(department);
        return department;
    }

    public void Update(string name, string? address, string? phone)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Branch name is required.");
        Name = name.Trim();
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    public void Deactivate()
    {
        if (IsMain) throw new DomainException("The main branch cannot be deactivated.");
        IsActive = false;
    }

    public void Activate() => IsActive = true;
}

/// <summary>A section of a branch (Chemistry, Hematology, …) used to organize the bench.</summary>
public sealed class Department : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;

    private Department() { } // EF

    internal Department(Guid id, Guid tenantId, Guid branchId, string code, string name)
        : base(id)
    {
        TenantId = tenantId;
        BranchId = branchId;
        Code = code;
        Name = name.Trim();
    }
}

public sealed record BranchCreated(Guid BranchId, Guid TenantId, string Code) : DomainEvent, ITenantEvent;
