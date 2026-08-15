using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Catalog;

namespace SkyLIS.Application.Catalog;

public sealed record ConditionInput(string Name, int? DelayMinutes, string CompatibilityGroup);

/// <summary>P03.4: define a sample type with its condition tree and compatibility groups.</summary>
public sealed record CreateSampleTypeCommand(
    string Name,
    string ContainerName,
    IReadOnlyList<ConditionInput> Conditions) : ICommand<SampleTypeDto>, IRequirePermission
{
    public string Permission => "catalog.sampletype.create";
}

public sealed record SampleTypeDto(
    Guid Id, string Name, string ContainerName, IReadOnlyList<ConditionDto> Conditions);

public sealed record ConditionDto(Guid Id, string Name, int? DelayMinutes, string CompatibilityGroup);

internal sealed class CreateSampleTypeValidator : AbstractValidator<CreateSampleTypeCommand>
{
    public CreateSampleTypeValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.ContainerName).NotEmpty().MaximumLength(80);
        RuleForEach(x => x.Conditions).ChildRules(condition =>
        {
            condition.RuleFor(c => c.Name).NotEmpty().MaximumLength(80);
            condition.RuleFor(c => c.CompatibilityGroup).NotEmpty().MaximumLength(40);
            condition.RuleFor(c => c.DelayMinutes).InclusiveBetween(0, 1440).When(c => c.DelayMinutes.HasValue);
        });
    }
}

internal sealed class CreateSampleTypeHandler : IRequestHandler<CreateSampleTypeCommand, SampleTypeDto>
{
    private readonly ISampleTypeRepository _sampleTypes;
    private readonly ITenantContext _tenant;

    public CreateSampleTypeHandler(ISampleTypeRepository sampleTypes, ITenantContext tenant)
    {
        _sampleTypes = sampleTypes;
        _tenant = tenant;
    }

    public Task<SampleTypeDto> Handle(CreateSampleTypeCommand request, CancellationToken ct)
    {
        var sampleType = SampleType.Create(
            Guid.CreateVersion7(), _tenant.TenantId, request.Name, request.ContainerName);
        foreach (var condition in request.Conditions)
            sampleType.AddCondition(Guid.CreateVersion7(), condition.Name, condition.DelayMinutes, condition.CompatibilityGroup);

        _sampleTypes.Add(sampleType);
        return Task.FromResult(new SampleTypeDto(
            sampleType.Id, sampleType.Name, sampleType.ContainerName,
            sampleType.Conditions.Select(c =>
                new ConditionDto(c.Id, c.Name, c.DelayMinutes, c.CompatibilityGroup)).ToList()));
    }
}

/// <summary>P03.3: submit a draft tenant test for Lab Director review.</summary>
public sealed record SubmitTestForReviewCommand(Guid TestId) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "catalog.test.update";
}

internal sealed class SubmitTestForReviewHandler : IRequestHandler<SubmitTestForReviewCommand, Unit>
{
    private readonly ILabTestRepository _tests;

    public SubmitTestForReviewHandler(ILabTestRepository tests) => _tests = tests;

    public async Task<Unit> Handle(SubmitTestForReviewCommand request, CancellationToken ct)
    {
        var test = await _tests.GetAsync(request.TestId, ct)
            ?? throw new NotFoundException("LabTest", request.TestId);
        test.SubmitForReview();
        return Unit.Value;
    }
}

/// <summary>P03.3: Lab Director approves the test; it activates (price gate enforced in domain).</summary>
public sealed record ApproveTestCommand(Guid TestId) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "catalog.test.approve";
}

internal sealed class ApproveTestHandler : IRequestHandler<ApproveTestCommand, Unit>
{
    private readonly ILabTestRepository _tests;

    public ApproveTestHandler(ILabTestRepository tests) => _tests = tests;

    public async Task<Unit> Handle(ApproveTestCommand request, CancellationToken ct)
    {
        var test = await _tests.GetAsync(request.TestId, ct)
            ?? throw new NotFoundException("LabTest", request.TestId);
        test.Approve();
        return Unit.Value;
    }
}
