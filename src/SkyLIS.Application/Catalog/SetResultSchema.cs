using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Catalog;

namespace SkyLIS.Application.Catalog;

/// <summary>P03.3 Result-schema tab: define the numeric schema a test needs before results can be entered.</summary>
public sealed record SetResultSchemaCommand(
    Guid TestId, string Unit, decimal? RefLow, decimal? RefHigh,
    decimal? CriticalLow, decimal? CriticalHigh, decimal? AbsurdLow, decimal? AbsurdHigh,
    bool AutoVerify, decimal? DeltaThresholdPercent) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "catalog.test.update";
}

internal sealed class SetResultSchemaValidator : AbstractValidator<SetResultSchemaCommand>
{
    public SetResultSchemaValidator()
    {
        RuleFor(x => x.TestId).NotEmpty();
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(20);
    }
}

internal sealed class SetResultSchemaHandler : IRequestHandler<SetResultSchemaCommand, Unit>
{
    private readonly ILabTestRepository _tests;

    public SetResultSchemaHandler(ILabTestRepository tests) => _tests = tests;

    public async Task<Unit> Handle(SetResultSchemaCommand request, CancellationToken ct)
    {
        var test = await _tests.GetAsync(request.TestId, ct)
            ?? throw new NotFoundException("LabTest", request.TestId);
        test.SetResultSchema(ResultSchema.Of(
            request.Unit, request.RefLow, request.RefHigh, request.CriticalLow, request.CriticalHigh,
            request.AbsurdLow, request.AbsurdHigh, request.AutoVerify, request.DeltaThresholdPercent));
        return Unit.Value;
    }
}
