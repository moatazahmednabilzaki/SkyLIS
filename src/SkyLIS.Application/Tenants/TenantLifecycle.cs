using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Tenants;

// P01.1: tenant lifecycle actions (Trial → Active → PastDue → Suspended → Offboarded).
// Transitions are guarded inside the aggregate; suspended tenants cannot sign in.

/// <summary>Activate a Trial tenant or resume a Suspended one.</summary>
public sealed record ActivateTenantCommand(Guid TenantId) : ICommand<Unit>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.tenant.manage";
}

internal sealed class ActivateTenantHandler : IRequestHandler<ActivateTenantCommand, Unit>
{
    private readonly ITenantRepository _tenants;
    public ActivateTenantHandler(ITenantRepository tenants) => _tenants = tenants;

    public async Task<Unit> Handle(ActivateTenantCommand request, CancellationToken ct)
    {
        var tenant = await _tenants.GetAsync(request.TenantId, ct)
            ?? throw new NotFoundException("Tenant", request.TenantId);
        tenant.Activate();
        return Unit.Value;
    }
}

public sealed record SuspendTenantCommand(Guid TenantId, string Reason) : ICommand<Unit>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.tenant.manage";
}

internal sealed class SuspendTenantValidator : AbstractValidator<SuspendTenantCommand>
{
    public SuspendTenantValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500)
            .WithMessage("A suspension reason is mandatory.");
    }
}

internal sealed class SuspendTenantHandler : IRequestHandler<SuspendTenantCommand, Unit>
{
    private readonly ITenantRepository _tenants;
    public SuspendTenantHandler(ITenantRepository tenants) => _tenants = tenants;

    public async Task<Unit> Handle(SuspendTenantCommand request, CancellationToken ct)
    {
        var tenant = await _tenants.GetAsync(request.TenantId, ct)
            ?? throw new NotFoundException("Tenant", request.TenantId);
        tenant.Suspend(request.Reason);
        return Unit.Value;
    }
}

public sealed record OffboardTenantCommand(Guid TenantId) : ICommand<Unit>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.tenant.manage";
}

internal sealed class OffboardTenantHandler : IRequestHandler<OffboardTenantCommand, Unit>
{
    private readonly ITenantRepository _tenants;
    public OffboardTenantHandler(ITenantRepository tenants) => _tenants = tenants;

    public async Task<Unit> Handle(OffboardTenantCommand request, CancellationToken ct)
    {
        var tenant = await _tenants.GetAsync(request.TenantId, ct)
            ?? throw new NotFoundException("Tenant", request.TenantId);
        tenant.Offboard();
        return Unit.Value;
    }
}
