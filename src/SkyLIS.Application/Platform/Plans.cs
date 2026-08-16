using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Platform;

namespace SkyLIS.Application.Platform;

public sealed record PlanDto(
    Guid Id, string Code, string Name, decimal MonthlyPrice, string Currency,
    int MaxUsers, int MaxBranches, int MonthlyReportQuota, bool IsActive);

public interface IPlanQueries
{
    Task<IReadOnlyList<PlanDto>> ListAsync(CancellationToken ct = default);
}

public sealed record ListPlansQuery : IQuery<IReadOnlyList<PlanDto>>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.masterdata.read";
}

internal sealed class ListPlansHandler : IRequestHandler<ListPlansQuery, IReadOnlyList<PlanDto>>
{
    private readonly IPlanQueries _queries;
    public ListPlansHandler(IPlanQueries queries) => _queries = queries;
    public Task<IReadOnlyList<PlanDto>> Handle(ListPlansQuery request, CancellationToken ct) =>
        _queries.ListAsync(ct);
}

/// <summary>P01.3: create or update a plan (the plan builder).</summary>
public sealed record UpsertPlanCommand(
    string Code, string Name, decimal MonthlyPrice, string Currency,
    int MaxUsers, int MaxBranches, int MonthlyReportQuota)
    : ICommand<Guid>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.masterdata.manage";
}

internal sealed class UpsertPlanValidator : AbstractValidator<UpsertPlanCommand>
{
    public UpsertPlanValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.MonthlyPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.MaxUsers).GreaterThanOrEqualTo(1);
        RuleFor(x => x.MaxBranches).GreaterThanOrEqualTo(1);
        RuleFor(x => x.MonthlyReportQuota).GreaterThanOrEqualTo(1);
    }
}

internal sealed class UpsertPlanHandler : IRequestHandler<UpsertPlanCommand, Guid>
{
    private readonly IPlanRepository _plans;
    public UpsertPlanHandler(IPlanRepository plans) => _plans = plans;

    public async Task<Guid> Handle(UpsertPlanCommand request, CancellationToken ct)
    {
        var existing = await _plans.GetByCodeAsync(request.Code.Trim().ToUpperInvariant(), ct);
        if (existing is not null)
        {
            existing.Update(request.Name, request.MonthlyPrice,
                request.MaxUsers, request.MaxBranches, request.MonthlyReportQuota);
            return existing.Id;
        }

        var plan = Plan.Create(
            Guid.CreateVersion7(), request.Code, request.Name, request.MonthlyPrice, request.Currency,
            request.MaxUsers, request.MaxBranches, request.MonthlyReportQuota);
        _plans.Add(plan);
        return plan.Id;
    }
}

/// <summary>P01.3: move a tenant to another plan; entitlements apply immediately.</summary>
public sealed record ChangeTenantPlanCommand(Guid TenantId, string PlanCode)
    : ICommand<Unit>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.tenant.manage";
}

internal sealed class ChangeTenantPlanValidator : AbstractValidator<ChangeTenantPlanCommand>
{
    public ChangeTenantPlanValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.PlanCode).NotEmpty().MaximumLength(40);
    }
}

internal sealed class ChangeTenantPlanHandler : IRequestHandler<ChangeTenantPlanCommand, Unit>
{
    private readonly ITenantRepository _tenants;
    private readonly IPlanRepository _plans;

    public ChangeTenantPlanHandler(ITenantRepository tenants, IPlanRepository plans)
    {
        _tenants = tenants;
        _plans = plans;
    }

    public async Task<Unit> Handle(ChangeTenantPlanCommand request, CancellationToken ct)
    {
        var plan = await _plans.GetByCodeAsync(request.PlanCode.Trim().ToUpperInvariant(), ct);
        if (plan is null || !plan.IsActive)
            throw new NotFoundException("Plan", request.PlanCode);
        var tenant = await _tenants.GetAsync(request.TenantId, ct)
            ?? throw new NotFoundException("Tenant", request.TenantId);
        tenant.ChangePlan(plan.Code);
        return Unit.Value;
    }
}

/// <summary>§8: seat/branch quota checks shared by the consuming handlers.</summary>
public static class Entitlements
{
    public static async Task<Plan> RequirePlanAsync(
        ITenantRepository tenants, IPlanRepository plans, Guid tenantId, CancellationToken ct)
    {
        var tenant = await tenants.GetAsync(tenantId, ct)
            ?? throw new NotFoundException("Tenant", tenantId);
        return await plans.GetByCodeAsync(tenant.PlanCode, ct)
            ?? throw new NotFoundException("Plan", tenant.PlanCode);
    }
}

// ---------- P01.5: platform read-only tenant user monitor ----------

public sealed record MonitoredUserDto(
    string UserName, string FullName, IReadOnlyCollection<string> Roles,
    string Status, DateTimeOffset? LastLoginAtUtc);

public interface ITenantUserMonitorQueries
{
    Task<IReadOnlyList<MonitoredUserDto>> ListAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>P01.5: the platform sees WHO has access at a tenant — read-only, PHI-free.</summary>
public sealed record GetTenantUsersQuery(Guid TenantId)
    : IQuery<IReadOnlyList<MonitoredUserDto>>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.tenant.read";
}

internal sealed class GetTenantUsersHandler : IRequestHandler<GetTenantUsersQuery, IReadOnlyList<MonitoredUserDto>>
{
    private readonly ITenantUserMonitorQueries _queries;
    public GetTenantUsersHandler(ITenantUserMonitorQueries queries) => _queries = queries;
    public Task<IReadOnlyList<MonitoredUserDto>> Handle(GetTenantUsersQuery request, CancellationToken ct) =>
        _queries.ListAsync(request.TenantId, ct);
}
