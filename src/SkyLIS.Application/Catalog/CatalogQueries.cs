using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Catalog;

public sealed record SampleTypeListDto(
    Guid Id, string Name, string ContainerName, IReadOnlyList<ConditionDto> Conditions);

public sealed record TestListDto(
    Guid Id, string Code, string Name, string Department, string Status, string Origin,
    decimal? Price, string? Currency, Guid SampleTypeId, Guid? RequiredConditionId, bool HasResultSchema);

/// <summary>Read side for the catalog pages and the visit-registration test picker.</summary>
public interface ICatalogQueries
{
    Task<IReadOnlyList<SampleTypeListDto>> ListSampleTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TestListDto>> ListTestsAsync(string? status, CancellationToken ct = default);
}

public sealed record ListSampleTypesQuery : IQuery<IReadOnlyList<SampleTypeListDto>>, IRequirePermission
{
    public string Permission => "catalog.catalog.read";
}

internal sealed class ListSampleTypesHandler : IRequestHandler<ListSampleTypesQuery, IReadOnlyList<SampleTypeListDto>>
{
    private readonly ICatalogQueries _queries;
    public ListSampleTypesHandler(ICatalogQueries queries) => _queries = queries;

    public Task<IReadOnlyList<SampleTypeListDto>> Handle(ListSampleTypesQuery request, CancellationToken ct) =>
        _queries.ListSampleTypesAsync(ct);
}

public sealed record ListTestsQuery(string? Status) : IQuery<IReadOnlyList<TestListDto>>, IRequirePermission
{
    public string Permission => "catalog.catalog.read";
}

internal sealed class ListTestsHandler : IRequestHandler<ListTestsQuery, IReadOnlyList<TestListDto>>
{
    private readonly ICatalogQueries _queries;
    public ListTestsHandler(ICatalogQueries queries) => _queries = queries;

    public Task<IReadOnlyList<TestListDto>> Handle(ListTestsQuery request, CancellationToken ct) =>
        _queries.ListTestsAsync(request.Status, ct);
}
