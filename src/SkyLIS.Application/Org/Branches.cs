using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Org;

namespace SkyLIS.Application.Org;

public sealed record DepartmentDto(Guid Id, string Code, string Name);

public sealed record BranchDto(
    Guid Id, string Code, string Name, string? Address, string? Phone,
    bool IsMain, bool IsActive, IReadOnlyList<DepartmentDto> Departments);

/// <summary>Read side for the Branches & Departments page (P03.2).</summary>
public interface IBranchQueries
{
    Task<IReadOnlyList<BranchDto>> ListAsync(CancellationToken ct = default);
}

public sealed record ListBranchesQuery : IQuery<IReadOnlyList<BranchDto>>, IRequirePermission
{
    public string Permission => "org.branch.read";
}

internal sealed class ListBranchesHandler : IRequestHandler<ListBranchesQuery, IReadOnlyList<BranchDto>>
{
    private readonly IBranchQueries _queries;
    public ListBranchesHandler(IBranchQueries queries) => _queries = queries;

    public Task<IReadOnlyList<BranchDto>> Handle(ListBranchesQuery request, CancellationToken ct) =>
        _queries.ListAsync(ct);
}

/// <summary>P03.2: open an additional branch (the MAIN branch ships with provisioning).</summary>
public sealed record CreateBranchCommand(
    string Code, string Name, string? Address, string? Phone) : ICommand<Guid>, IRequirePermission
{
    public string Permission => "org.branch.manage";
}

internal sealed class CreateBranchValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MinimumLength(2).MaximumLength(10)
            .Matches("^[A-Za-z0-9]+$").WithMessage("Branch code: letters and digits only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.Phone).MaximumLength(20);
    }
}

internal sealed class CreateBranchHandler : IRequestHandler<CreateBranchCommand, Guid>
{
    private readonly IBranchRepository _branches;
    private readonly ITenantRepository _tenants;
    private readonly IPlanRepository _plans;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public CreateBranchHandler(
        IBranchRepository branches, ITenantRepository tenants, IPlanRepository plans,
        ITenantContext tenant, IClock clock)
    {
        _branches = branches;
        _tenants = tenants;
        _plans = plans;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreateBranchCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _branches.CodeExistsAsync(code, ct))
            throw new ConflictException($"Branch code '{code}' is already in use.");

        // §8 branch quota: only ACTIVE branches count — deactivate one to open another.
        var plan = await Platform.Entitlements.RequirePlanAsync(_tenants, _plans, _tenant.TenantId, ct);
        if (await _branches.CountActiveAsync(ct) >= plan.MaxBranches)
            throw new Domain.Common.DomainException(
                $"The {plan.Code} plan allows {plan.MaxBranches} active branch(es); deactivate one or upgrade the plan.");

        var branch = Branch.Create(
            Guid.CreateVersion7(), _tenant.TenantId, code, request.Name,
            request.Address, request.Phone, isMain: false, _clock.UtcNow);
        _branches.Add(branch);
        return branch.Id;
    }
}

public sealed record AddDepartmentCommand(Guid BranchId, string Code, string Name) : ICommand<Guid>, IRequirePermission
{
    public string Permission => "org.branch.manage";
}

internal sealed class AddDepartmentValidator : AbstractValidator<AddDepartmentCommand>
{
    public AddDepartmentValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MinimumLength(2).MaximumLength(10)
            .Matches("^[A-Za-z0-9]+$").WithMessage("Department code: letters and digits only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
    }
}

internal sealed class AddDepartmentHandler : IRequestHandler<AddDepartmentCommand, Guid>
{
    private readonly IBranchRepository _branches;
    public AddDepartmentHandler(IBranchRepository branches) => _branches = branches;

    public async Task<Guid> Handle(AddDepartmentCommand request, CancellationToken ct)
    {
        var branch = await _branches.GetAsync(request.BranchId, ct)
            ?? throw new NotFoundException("Branch", request.BranchId);
        var department = branch.AddDepartment(Guid.CreateVersion7(), request.Code, request.Name);
        return department.Id;
    }
}

/// <summary>Deactivate/reactivate a branch (the MAIN branch is guarded in the domain).</summary>
public sealed record SetBranchActiveCommand(Guid BranchId, bool IsActive) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "org.branch.manage";
}

internal sealed class SetBranchActiveHandler : IRequestHandler<SetBranchActiveCommand, Unit>
{
    private readonly IBranchRepository _branches;
    public SetBranchActiveHandler(IBranchRepository branches) => _branches = branches;

    public async Task<Unit> Handle(SetBranchActiveCommand request, CancellationToken ct)
    {
        var branch = await _branches.GetAsync(request.BranchId, ct)
            ?? throw new NotFoundException("Branch", request.BranchId);
        if (request.IsActive) branch.Activate();
        else branch.Deactivate();
        return Unit.Value;
    }
}
