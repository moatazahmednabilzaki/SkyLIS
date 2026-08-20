using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Reports;

namespace SkyLIS.Application.Reports;

/// <summary>Outbound notification port (email/WhatsApp/print routing). Dev implementation logs.</summary>
public interface INotificationSender
{
    Task<bool> SendAsync(string channel, string destination, string subject, CancellationToken ct = default);
}

public sealed record DeliveryResultDto(Guid ReportId, string Channel, string Destination, string Outcome, string ReportStatus);

/// <summary>FR-REP-001: deliver a rendered report over one channel; every attempt is logged.</summary>
public sealed record DeliverReportCommand(Guid ReportId, string Channel, string Destination)
    : ICommand<DeliveryResultDto>, IRequirePermission
{
    public string Permission => "reports.report.deliver";
}

internal sealed class DeliverReportValidator : AbstractValidator<DeliverReportCommand>
{
    private static readonly string[] Channels = ["print", "email", "whatsapp", "portal"];

    public DeliverReportValidator()
    {
        RuleFor(x => x.ReportId).NotEmpty();
        RuleFor(x => x.Channel)
            .NotEmpty()
            .Must(c => Channels.Contains(c.ToLowerInvariant()))
            .WithMessage("Channel must be one of: print, email, whatsapp, portal.");
        RuleFor(x => x.Destination).NotEmpty().MaximumLength(200);
    }
}

internal sealed class DeliverReportHandler : IRequestHandler<DeliverReportCommand, DeliveryResultDto>
{
    private readonly ILabReportRepository _reports;
    private readonly INotificationSender _sender;
    private readonly IClock _clock;

    public DeliverReportHandler(ILabReportRepository reports, INotificationSender sender, IClock clock)
    {
        _reports = reports;
        _sender = sender;
        _clock = clock;
    }

    public async Task<DeliveryResultDto> Handle(DeliverReportCommand request, CancellationToken ct)
    {
        var report = await _reports.GetAsync(request.ReportId, ct)
            ?? throw new NotFoundException("LabReport", request.ReportId);

        var channel = request.Channel.ToLowerInvariant();
        var sent = await _sender.SendAsync(channel, request.Destination,
            $"Sky LIS report {report.ReportNumber} v{report.Version}", ct);

        report.RecordDelivery(
            Guid.CreateVersion7(), channel, request.Destination,
            sent ? DeliveryOutcome.Sent : DeliveryOutcome.Failed, _clock.UtcNow);

        return new DeliveryResultDto(
            report.Id, channel, request.Destination,
            sent ? "Sent" : "Failed", report.Status.ToString());
    }
}

// ---- Queries ----

public sealed record GetReportingWorklistQuery : IQuery<IReadOnlyList<ReportWorklistRowDto>>, IRequirePermission
{
    public string Permission => "reports.report.read";
}

internal sealed class GetReportingWorklistHandler
    : IRequestHandler<GetReportingWorklistQuery, IReadOnlyList<ReportWorklistRowDto>>
{
    private readonly IReportQueries _queries;
    public GetReportingWorklistHandler(IReportQueries queries) => _queries = queries;
    public Task<IReadOnlyList<ReportWorklistRowDto>> Handle(GetReportingWorklistQuery request, CancellationToken ct) =>
        _queries.WorklistAsync(ct);
}

public sealed record ReportArtifactDto(
    Guid ReportId, string ReportNumber, int Version, string Kind,
    string ContentHtml, byte[] ContentPdf, string ContentHash);

/// <summary>P10.2: faithful artifact retrieval for authorized viewing/printing.</summary>
public sealed record GetReportArtifactQuery(Guid ReportId) : IQuery<ReportArtifactDto>, IRequirePermission
{
    public string Permission => "reports.report.read";
}

internal sealed class GetReportArtifactHandler : IRequestHandler<GetReportArtifactQuery, ReportArtifactDto>
{
    private readonly ILabReportRepository _reports;
    public GetReportArtifactHandler(ILabReportRepository reports) => _reports = reports;

    public async Task<ReportArtifactDto> Handle(GetReportArtifactQuery request, CancellationToken ct)
    {
        var report = await _reports.GetAsync(request.ReportId, ct)
            ?? throw new NotFoundException("LabReport", request.ReportId);
        return new ReportArtifactDto(
            report.Id, report.ReportNumber, report.Version, report.Kind.ToString(),
            report.ContentHtml, report.ContentPdf, report.ContentHash);
    }
}
