using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Tenants;

namespace SkyLIS.Application.Tenants;

/// <summary>
/// FR-TEN-010: provision a new tenant from the Admin Portal onboarding wizard, including
/// the initial Tenant Admin (P01.2 step 4). The admin user itself is created by the
/// TenantProvisioned outbox consumer under the new tenant's own context; the event
/// carries only the password HASH.
/// </summary>
public sealed record ProvisionTenantCommand(
    string LegalName,
    string Subdomain,
    string CountryCode,
    string PlanCode,
    IsolationTier IsolationTier,
    string AdminUserName,
    string AdminFullName,
    string AdminPassword) : ICommand<Guid>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.tenant.provision";
}

internal sealed class ProvisionTenantValidator : AbstractValidator<ProvisionTenantCommand>
{
    private static readonly string[] ReservedSubdomains = ["www", "api", "admin", "console", "app", "portal"];

    public ProvisionTenantValidator()
    {
        RuleFor(x => x.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Subdomain)
            .NotEmpty().MinimumLength(3).MaximumLength(40)
            .Matches("^[a-z0-9-]+$").WithMessage("Subdomain may contain lowercase letters, digits, and hyphens only.")
            .Must(s => !ReservedSubdomains.Contains(s)).WithMessage("This subdomain is reserved.");
        RuleFor(x => x.CountryCode).NotEmpty().Length(2);
        RuleFor(x => x.PlanCode).NotEmpty().MaximumLength(40);
        RuleFor(x => x.AdminUserName).NotEmpty().MinimumLength(3).MaximumLength(60);
        RuleFor(x => x.AdminFullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AdminPassword).NotEmpty().MinimumLength(12)
            .WithMessage("Password policy: at least 12 characters (§4.3).");
    }
}

internal sealed class ProvisionTenantHandler : IRequestHandler<ProvisionTenantCommand, Guid>
{
    private readonly ITenantRepository _tenants;
    private readonly Users.IPasswordHasher _hasher;
    private readonly IClock _clock;

    public ProvisionTenantHandler(ITenantRepository tenants, Users.IPasswordHasher hasher, IClock clock)
    {
        _tenants = tenants;
        _hasher = hasher;
        _clock = clock;
    }

    public async Task<Guid> Handle(ProvisionTenantCommand request, CancellationToken ct)
    {
        if (await _tenants.SubdomainExistsAsync(request.Subdomain.ToLowerInvariant(), ct))
            throw new ConflictException($"Subdomain '{request.Subdomain}' is already taken.");

        var tenant = Tenant.Provision(
            Guid.CreateVersion7(), request.LegalName, request.Subdomain, request.CountryCode,
            request.PlanCode, request.IsolationTier,
            request.AdminUserName, request.AdminFullName, _hasher.Hash(request.AdminPassword),
            _clock.UtcNow);

        _tenants.Add(tenant);
        return tenant.Id;
    }
}
