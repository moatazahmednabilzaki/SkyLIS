using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Results;

namespace SkyLIS.Application.Results;

public sealed record EnteredResultDto(
    Guid ResultId, string TestCode, decimal Value, string Unit, string Flag,
    bool DeltaFlagged, decimal? PreviousValue, string Status, bool AutoVerified, bool CriticalFlagged);

/// <summary>
/// FR-RES-001 (P09.1): enter one result for a visit test line. Rules run in the domain:
/// absurd guard, range/critical flags, delta vs the patient's previous value, and
/// auto-verification for clean in-range results.
/// </summary>
public sealed record EnterResultCommand(Guid VisitId, Guid VisitTestId, decimal Value)
    : ICommand<EnteredResultDto>, IRequirePermission
{
    public string Permission => "results.result.enter";
}

internal sealed class EnterResultValidator : AbstractValidator<EnterResultCommand>
{
    public EnterResultValidator()
    {
        RuleFor(x => x.VisitId).NotEmpty();
        RuleFor(x => x.VisitTestId).NotEmpty();
    }
}

internal sealed class EnterResultHandler : IRequestHandler<EnterResultCommand, EnteredResultDto>
{
    private readonly IVisitRepository _visits;
    private readonly ILabTestRepository _tests;
    private readonly ITestResultRepository _results;
    private readonly IResultQueries _queries;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public EnterResultHandler(
        IVisitRepository visits, ILabTestRepository tests, ITestResultRepository results,
        IResultQueries queries, ITenantContext tenant, ICurrentUser user, IClock clock)
    {
        _visits = visits;
        _tests = tests;
        _results = results;
        _queries = queries;
        _tenant = tenant;
        _user = user;
        _clock = clock;
    }

    public async Task<EnteredResultDto> Handle(EnterResultCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetAsync(request.VisitId, ct)
            ?? throw new NotFoundException("Visit", request.VisitId);
        var line = visit.Tests.FirstOrDefault(t => t.Id == request.VisitTestId)
            ?? throw new NotFoundException("VisitTest", request.VisitTestId);
        if (await _results.GetActiveByLineAsync(line.Id, ct) is not null)
            throw new ConflictException($"An active result already exists for {line.TestCode}; order a rerun first.");

        var test = await _tests.GetAsync(line.TestId, ct)
            ?? throw new NotFoundException("LabTest", line.TestId);

        var previous = await _queries.GetPreviousValueAsync(visit.PatientId, line.TestId, ct);
        var evaluation = ResultEvaluator.Evaluate(test, request.Value, previous);

        // Line transition first: enforces "sample received" before any result exists.
        visit.MarkTestEntered(line.Id);

        var result = TestResult.Enter(
            Guid.CreateVersion7(), _tenant.TenantId, visit.Id, line.Id, visit.PatientId,
            line.TestCode, request.Value, test.ResultSchema!.Unit, evaluation,
            _user.UserId ?? Guid.Empty, _clock.UtcNow);
        if (result.Status == ResultStatus.TechnicallyValid)
            visit.MarkTestTechnicallyValid(line.Id);

        _results.Add(result);

        return new EnteredResultDto(
            result.Id, result.TestCode, result.Value, result.Unit, result.Flag.ToString(),
            result.DeltaFlagged, result.PreviousValue, result.Status.ToString(),
            AutoVerified: result.Status == ResultStatus.TechnicallyValid,
            CriticalFlagged: result.Critical is not null);
    }
}
