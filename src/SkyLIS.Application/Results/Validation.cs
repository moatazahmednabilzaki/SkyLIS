using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Results;

/// <summary>FR-RES-010 (P09.2): supervisor accepts a flagged result to Technically Valid.</summary>
public sealed record AcceptTechnicalCommand(Guid ResultId) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "results.result.validateTechnical";
}

internal sealed class AcceptTechnicalHandler : IRequestHandler<AcceptTechnicalCommand, Unit>
{
    private readonly ITestResultRepository _results;
    private readonly IVisitRepository _visits;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public AcceptTechnicalHandler(ITestResultRepository results, IVisitRepository visits, ICurrentUser user, IClock clock)
    {
        _results = results;
        _visits = visits;
        _user = user;
        _clock = clock;
    }

    public async Task<Unit> Handle(AcceptTechnicalCommand request, CancellationToken ct)
    {
        var result = await _results.GetAsync(request.ResultId, ct)
            ?? throw new NotFoundException("TestResult", request.ResultId);
        var visit = await _visits.GetAsync(result.VisitId, ct)
            ?? throw new NotFoundException("Visit", result.VisitId);

        result.AcceptTechnical(_user.UserId ?? Guid.Empty, _clock.UtcNow);
        visit.MarkTestTechnicallyValid(result.VisitTestId);
        return Unit.Value;
    }
}

/// <summary>P09.1/P09.2: order a rerun — voids the result, the line returns to Pending.</summary>
public sealed record OrderRerunCommand(Guid ResultId, string Reason) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "results.result.enter";
}

internal sealed class OrderRerunValidator : AbstractValidator<OrderRerunCommand>
{
    public OrderRerunValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
    }
}

internal sealed class OrderRerunHandler : IRequestHandler<OrderRerunCommand, Unit>
{
    private readonly ITestResultRepository _results;
    private readonly IVisitRepository _visits;

    public OrderRerunHandler(ITestResultRepository results, IVisitRepository visits)
    {
        _results = results;
        _visits = visits;
    }

    public async Task<Unit> Handle(OrderRerunCommand request, CancellationToken ct)
    {
        var result = await _results.GetAsync(request.ResultId, ct)
            ?? throw new NotFoundException("TestResult", request.ResultId);
        var visit = await _visits.GetAsync(result.VisitId, ct)
            ?? throw new NotFoundException("Visit", result.VisitId);

        result.OrderRerun(request.Reason);
        visit.MarkTestRerun(result.VisitTestId);
        return Unit.Value;
    }
}

/// <summary>
/// FR-RES-020 (P09.3): medical sign-out with e-signature. The signature binds signer
/// identity, record, timestamp, and the content hash of (value|unit|flag) — FR-SYS-002.
/// SoD (enterer ≠ validator) is enforced inside the aggregate.
/// In Development the re-authentication step is represented by the declared intent;
/// the OIDC authority adds real re-auth in later phases.
/// </summary>
public sealed record ValidateMedicalCommand(Guid ResultId, string? InterpretiveComment, string SignatureIntent)
    : ICommand<Unit>, IRequirePermission
{
    public string Permission => "results.result.validateMedical";
}

internal sealed class ValidateMedicalValidator : AbstractValidator<ValidateMedicalCommand>
{
    public ValidateMedicalValidator()
    {
        RuleFor(x => x.SignatureIntent).NotEmpty().WithMessage("The signature intent declaration is required (FR-SYS-002).");
        RuleFor(x => x.InterpretiveComment).MaximumLength(2000);
    }
}

internal sealed class ValidateMedicalHandler : IRequestHandler<ValidateMedicalCommand, Unit>
{
    private readonly ITestResultRepository _results;
    private readonly IVisitRepository _visits;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public ValidateMedicalHandler(ITestResultRepository results, IVisitRepository visits, ICurrentUser user, IClock clock)
    {
        _results = results;
        _visits = visits;
        _user = user;
        _clock = clock;
    }

    public async Task<Unit> Handle(ValidateMedicalCommand request, CancellationToken ct)
    {
        var result = await _results.GetAsync(request.ResultId, ct)
            ?? throw new NotFoundException("TestResult", request.ResultId);
        var visit = await _visits.GetAsync(result.VisitId, ct)
            ?? throw new NotFoundException("Visit", result.VisitId);

        var content = $"{result.Id}|{result.Value}|{result.Unit}|{result.Flag}|{request.SignatureIntent}|{_user.UserId}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        result.ValidateMedical(_user.UserId ?? Guid.Empty, request.InterpretiveComment, hash, _clock.UtcNow);
        visit.MarkTestMedicallyValid(result.VisitTestId);
        return Unit.Value;
    }
}

/// <summary>FR-RES-030 (P09.4): document the critical-value call with read-back evidence.</summary>
public sealed record DocumentCriticalCallCommand(Guid ResultId, string CalledPerson, string Phone, bool ReadBackConfirmed)
    : ICommand<Unit>, IRequirePermission
{
    public string Permission => "results.result.enter";
}

internal sealed class DocumentCriticalCallValidator : AbstractValidator<DocumentCriticalCallCommand>
{
    public DocumentCriticalCallValidator()
    {
        RuleFor(x => x.CalledPerson).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(20);
    }
}

internal sealed class DocumentCriticalCallHandler : IRequestHandler<DocumentCriticalCallCommand, Unit>
{
    private readonly ITestResultRepository _results;
    private readonly IClock _clock;

    public DocumentCriticalCallHandler(ITestResultRepository results, IClock clock)
    {
        _results = results;
        _clock = clock;
    }

    public async Task<Unit> Handle(DocumentCriticalCallCommand request, CancellationToken ct)
    {
        var result = await _results.GetAsync(request.ResultId, ct)
            ?? throw new NotFoundException("TestResult", request.ResultId);
        result.DocumentCriticalCall(request.CalledPerson, request.Phone, request.ReadBackConfirmed, _clock.UtcNow);
        return Unit.Value;
    }
}
