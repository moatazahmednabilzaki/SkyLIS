using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Visits;

namespace SkyLIS.Application.Visits;

/// <summary>P08.2: phlebotomist confirms collection of a sample (scan-to-confirm).</summary>
public sealed record CollectSampleCommand(Guid VisitId, Guid SampleId) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "samples.sample.collect";
}

internal sealed class CollectSampleHandler : IRequestHandler<CollectSampleCommand, Unit>
{
    private readonly IVisitRepository _visits;
    private readonly IClock _clock;

    public CollectSampleHandler(IVisitRepository visits, IClock clock)
    {
        _visits = visits;
        _clock = clock;
    }

    public async Task<Unit> Handle(CollectSampleCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetAsync(request.VisitId, ct)
            ?? throw new NotFoundException("Visit", request.VisitId);
        visit.CollectSample(request.SampleId, _clock.UtcNow);
        return Unit.Value;
    }
}

/// <summary>P07.2: accessioning accepts a sample into the laboratory.</summary>
public sealed record ReceiveSampleCommand(Guid VisitId, Guid SampleId) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "samples.sample.receive";
}

internal sealed class ReceiveSampleHandler : IRequestHandler<ReceiveSampleCommand, Unit>
{
    private readonly IVisitRepository _visits;
    private readonly IClock _clock;

    public ReceiveSampleHandler(IVisitRepository visits, IClock clock)
    {
        _visits = visits;
        _clock = clock;
    }

    public async Task<Unit> Handle(ReceiveSampleCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetAsync(request.VisitId, ct)
            ?? throw new NotFoundException("Visit", request.VisitId);
        visit.ReceiveSample(request.SampleId, _clock.UtcNow);
        return Unit.Value;
    }
}

/// <summary>P07.3: controlled rejection with a coded reason; spawns the recollection sample.</summary>
public sealed record RejectSampleCommand(Guid VisitId, Guid SampleId, string ReasonCode)
    : ICommand<Guid>, IRequirePermission
{
    public string Permission => "samples.sample.reject";
}

internal sealed class RejectSampleValidator : AbstractValidator<RejectSampleCommand>
{
    public RejectSampleValidator()
    {
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(60);
    }
}

internal sealed class RejectSampleHandler : IRequestHandler<RejectSampleCommand, Guid>
{
    private readonly IVisitRepository _visits;
    private readonly Org.ITenantSettingQueries _settings;
    private readonly IClock _clock;

    public RejectSampleHandler(IVisitRepository visits, Org.ITenantSettingQueries settings, IClock clock)
    {
        _visits = visits;
        _settings = settings;
        _clock = clock;
    }

    public async Task<Guid> Handle(RejectSampleCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetAsync(request.VisitId, ct)
            ?? throw new NotFoundException("Visit", request.VisitId);

        // FR-SYS-004: when the tenant defines a rejection vocabulary, only coded reasons pass.
        var vocabulary = await _settings.GetValueAsync("rejection.reasons", ct);
        if (vocabulary is not null)
        {
            var allowed = vocabulary.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!allowed.Contains(request.ReasonCode.Trim(), StringComparer.OrdinalIgnoreCase))
                throw new Domain.Common.DomainException(
                    $"Rejection reason '{request.ReasonCode}' is not in the tenant vocabulary: {vocabulary}.");
        }

        var rejected = visit.Samples.First(s => s.Id == request.SampleId);
        var recollection = visit.RejectSample(
            request.SampleId, request.ReasonCode,
            Guid.CreateVersion7(), rejected.Barcode + "R", _clock.UtcNow);
        return recollection.Id;
    }
}
