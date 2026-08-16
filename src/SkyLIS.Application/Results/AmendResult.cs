using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Results;

namespace SkyLIS.Application.Results;

public sealed record AmendedResultDto(
    Guid ResultId, string TestCode, decimal OldValue, decimal NewValue, string Flag,
    bool CriticalFlagged, string AmendmentReason);

/// <summary>
/// P09.5: amend a medically valid result. Re-evaluates flags against the schema, re-signs
/// (FR-SYS-002), preserves the old value on the record, and opens a fresh critical cycle
/// when the corrected value is critical. Subsequent reports render as AMENDED.
/// </summary>
public sealed record AmendResultCommand(Guid ResultId, decimal NewValue, string Reason, string SignatureIntent)
    : ICommand<AmendedResultDto>, IRequirePermission
{
    public string Permission => "results.result.amend";
}

internal sealed class AmendResultValidator : AbstractValidator<AmendResultCommand>
{
    public AmendResultValidator()
    {
        RuleFor(x => x.ResultId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300)
            .WithMessage("An amendment reason is mandatory (P09.5).");
        RuleFor(x => x.SignatureIntent).NotEmpty()
            .WithMessage("The signature intent declaration is required (FR-SYS-002).");
    }
}

internal sealed class AmendResultHandler : IRequestHandler<AmendResultCommand, AmendedResultDto>
{
    private readonly ITestResultRepository _results;
    private readonly IVisitRepository _visits;
    private readonly ILabTestRepository _tests;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public AmendResultHandler(
        ITestResultRepository results, IVisitRepository visits, ILabTestRepository tests,
        ICurrentUser user, IClock clock)
    {
        _results = results;
        _visits = visits;
        _tests = tests;
        _user = user;
        _clock = clock;
    }

    public async Task<AmendedResultDto> Handle(AmendResultCommand request, CancellationToken ct)
    {
        var result = await _results.GetAsync(request.ResultId, ct)
            ?? throw new NotFoundException("TestResult", request.ResultId);
        var visit = await _visits.GetAsync(result.VisitId, ct)
            ?? throw new NotFoundException("Visit", result.VisitId);
        var line = visit.Tests.FirstOrDefault(t => t.Id == result.VisitTestId)
            ?? throw new NotFoundException("VisitTest", result.VisitTestId);
        var test = await _tests.GetAsync(line.TestId, ct)
            ?? throw new NotFoundException("LabTest", line.TestId);

        var oldValue = result.Value;
        var evaluation = ResultEvaluator.Evaluate(test, request.NewValue, previousValue: oldValue);

        var content = $"{result.Id}|AMEND|{oldValue}->{request.NewValue}|{result.Unit}|{request.SignatureIntent}|{_user.UserId}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        result.Amend(request.NewValue, evaluation, request.Reason, _user.UserId ?? Guid.Empty, hash, _clock.UtcNow);

        return new AmendedResultDto(
            result.Id, result.TestCode, oldValue, result.Value, result.Flag.ToString(),
            CriticalFlagged: result.Critical is not null && result.Critical.State != CriticalState.Closed,
            result.AmendmentReason!);
    }
}
