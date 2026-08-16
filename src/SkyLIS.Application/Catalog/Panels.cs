using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Common;

namespace SkyLIS.Application.Catalog;

public sealed record PanelDto(
    Guid Id, string Code, string Name, decimal Price, string Currency, bool IsActive,
    IReadOnlyList<PanelMemberDto> Members);

public sealed record PanelMemberDto(Guid TestId, string TestCode, string TestName);

public interface IPanelQueries
{
    Task<IReadOnlyList<PanelDto>> ListAsync(CancellationToken ct = default);
}

public sealed record ListPanelsQuery : IQuery<IReadOnlyList<PanelDto>>, IRequirePermission
{
    public string Permission => "catalog.catalog.read";
}

internal sealed class ListPanelsHandler : IRequestHandler<ListPanelsQuery, IReadOnlyList<PanelDto>>
{
    private readonly IPanelQueries _queries;
    public ListPanelsHandler(IPanelQueries queries) => _queries = queries;
    public Task<IReadOnlyList<PanelDto>> Handle(ListPanelsQuery request, CancellationToken ct) =>
        _queries.ListAsync(ct);
}

/// <summary>P03.5: define a panel — a bundle of ACTIVE tests at a bundle price.</summary>
public sealed record CreatePanelCommand(
    string Code, string Name, decimal Price, string Currency, IReadOnlyList<Guid> TestIds)
    : ICommand<Guid>, IRequirePermission
{
    public string Permission => "catalog.test.create";
}

internal sealed class CreatePanelValidator : AbstractValidator<CreatePanelCommand>
{
    public CreatePanelValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.TestIds).Must(ids => ids.Count >= 2)
            .WithMessage("A panel bundles at least two tests.");
    }
}

internal sealed class CreatePanelHandler : IRequestHandler<CreatePanelCommand, Guid>
{
    private readonly IPanelRepository _panels;
    private readonly ILabTestRepository _tests;
    private readonly ITenantContext _tenant;

    public CreatePanelHandler(IPanelRepository panels, ILabTestRepository tests, ITenantContext tenant)
    {
        _panels = panels;
        _tests = tests;
        _tenant = tenant;
    }

    public async Task<Guid> Handle(CreatePanelCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _panels.CodeExistsAsync(code, ct))
            throw new ConflictException($"Panel code '{code}' already exists.");

        var members = await _tests.GetManyAsync(request.TestIds.Distinct().ToArray(), ct);
        var missing = request.TestIds.Except(members.Select(t => t.Id)).ToList();
        if (missing.Count > 0)
            throw new NotFoundException("LabTest", string.Join(", ", missing));
        var inactive = members.Where(t => t.Status != TestStatus.Active).Select(t => t.Code).ToList();
        if (inactive.Count > 0)
            throw new DomainException($"Panels bundle ACTIVE tests only; not active: {string.Join(", ", inactive)}.");

        var panel = Panel.Create(
            Guid.CreateVersion7(), _tenant.TenantId, code, request.Name,
            Money.Of(request.Price, request.Currency), request.TestIds.Distinct().ToList());
        _panels.Add(panel);
        return panel.Id;
    }
}
