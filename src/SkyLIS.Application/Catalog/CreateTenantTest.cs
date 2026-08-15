using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Common;

namespace SkyLIS.Application.Catalog;

/// <summary>
/// P03.3 "Add tenant test": a test that exists in this tenant only, never affected by
/// platform packs or pushes. Activates through review with a resolvable price.
/// </summary>
public sealed record CreateTenantTestCommand(
    string Code,
    string Name,
    string Department,
    Guid SampleTypeId,
    Guid? RequiredConditionId,
    decimal Price,
    string Currency) : ICommand<Guid>, IRequirePermission
{
    public string Permission => "catalog.test.create";
}

internal sealed class CreateTenantTestValidator : AbstractValidator<CreateTenantTestCommand>
{
    public CreateTenantTestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Department).NotEmpty().MaximumLength(80);
        RuleFor(x => x.SampleTypeId).NotEmpty();
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}

internal sealed class CreateTenantTestHandler : IRequestHandler<CreateTenantTestCommand, Guid>
{
    private readonly ILabTestRepository _tests;
    private readonly ISampleTypeRepository _sampleTypes;
    private readonly ITenantContext _tenant;

    public CreateTenantTestHandler(ILabTestRepository tests, ISampleTypeRepository sampleTypes, ITenantContext tenant)
    {
        _tests = tests;
        _sampleTypes = sampleTypes;
        _tenant = tenant;
    }

    public async Task<Guid> Handle(CreateTenantTestCommand request, CancellationToken ct)
    {
        if (await _tests.CodeExistsAsync(request.Code.Trim().ToUpperInvariant(), ct))
            throw new ConflictException($"Test code '{request.Code}' already exists in this tenant's catalogue.");

        _ = await _sampleTypes.GetAsync(request.SampleTypeId, ct)
            ?? throw new NotFoundException("SampleType", request.SampleTypeId);

        var test = LabTest.CreateTenantTest(
            Guid.CreateVersion7(), _tenant.TenantId, request.Code, request.Name, request.Department,
            request.SampleTypeId, request.RequiredConditionId, Money.Of(request.Price, request.Currency));

        _tests.Add(test);
        return test.Id;
    }
}

/// <summary>Activate a platform-pushed test by setting the local price (FR-MDM-071).</summary>
public sealed record ActivatePushedTestCommand(Guid TestId, decimal Price, string Currency) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "catalog.test.update";
}

internal sealed class ActivatePushedTestHandler : IRequestHandler<ActivatePushedTestCommand, Unit>
{
    private readonly ILabTestRepository _tests;

    public ActivatePushedTestHandler(ILabTestRepository tests) => _tests = tests;

    public async Task<Unit> Handle(ActivatePushedTestCommand request, CancellationToken ct)
    {
        var test = await _tests.GetAsync(request.TestId, ct)
            ?? throw new NotFoundException("LabTest", request.TestId);
        test.ActivatePushedTest(Money.Of(request.Price, request.Currency));
        return Unit.Value;
    }
}
