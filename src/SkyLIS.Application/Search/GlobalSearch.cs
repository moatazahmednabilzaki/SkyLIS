using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Search;

public sealed record SearchHitDto(string Kind, Guid Id, string Title, string Subtitle, Guid? NavigateId);

public sealed record GlobalSearchDto(
    IReadOnlyList<SearchHitDto> Patients,
    IReadOnlyList<SearchHitDto> Visits,
    IReadOnlyList<SearchHitDto> Samples,
    IReadOnlyList<SearchHitDto> Invoices,
    IReadOnlyList<SearchHitDto> Tests);

public interface ISearchQueries
{
    Task<GlobalSearchDto> SearchAsync(string term, CancellationToken ct = default);
}

/// <summary>
/// FR-SYS-008 global search (Ctrl+K): one query fans out over patients, visits, samples,
/// invoices, and catalog tests — top 5 hits per group, tenant-scoped like everything else.
/// </summary>
public sealed record GlobalSearchQuery(string Term) : IQuery<GlobalSearchDto>, IRequirePermission
{
    public string Permission => "orders.visit.read";
}

internal sealed class GlobalSearchValidator : AbstractValidator<GlobalSearchQuery>
{
    public GlobalSearchValidator()
    {
        RuleFor(x => x.Term).NotEmpty().MinimumLength(2).MaximumLength(80);
    }
}

internal sealed class GlobalSearchHandler : IRequestHandler<GlobalSearchQuery, GlobalSearchDto>
{
    private readonly ISearchQueries _queries;
    public GlobalSearchHandler(ISearchQueries queries) => _queries = queries;

    public Task<GlobalSearchDto> Handle(GlobalSearchQuery request, CancellationToken ct) =>
        _queries.SearchAsync(request.Term.Trim(), ct);
}
