using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Tenants;

public sealed record TenantDto(
    Guid Id, string LegalName, string Subdomain, string CountryCode,
    string PlanCode, string Status, DateTimeOffset CreatedAtUtc);

/// <summary>FR-TEN-001: tenant directory listing for the Admin Portal.</summary>
public sealed record GetTenantsQuery(string? Search) : IQuery<IReadOnlyList<TenantDto>>, IPlatformScoped, IRequirePermission
{
    public string Permission => "platform.tenant.read";
}

/// <summary>Read-side port implemented in Infrastructure with a direct DTO projection.</summary>
public interface ITenantQueries
{
    Task<IReadOnlyList<TenantDto>> ListAsync(string? search, CancellationToken ct = default);
}

internal sealed class GetTenantsHandler : IRequestHandler<GetTenantsQuery, IReadOnlyList<TenantDto>>
{
    private readonly ITenantQueries _queries;

    public GetTenantsHandler(ITenantQueries queries) => _queries = queries;

    public Task<IReadOnlyList<TenantDto>> Handle(GetTenantsQuery request, CancellationToken ct) =>
        _queries.ListAsync(request.Search, ct);
}
