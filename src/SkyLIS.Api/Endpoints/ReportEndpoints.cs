using MediatR;
using SkyLIS.Application.Reports;
using SkyLIS.Domain.Reports;

namespace SkyLIS.Api.Endpoints;

public static class ReportEndpoints
{
    public sealed record RenderReportRequest(ReportKind Kind);
    public sealed record DeliverReportRequest(string Channel, string Destination);

    public static RouteGroupBuilder MapReportEndpoints(this RouteGroupBuilder group)
    {
        var reports = group.MapGroup("/reports").RequireAuthorization().WithTags("Client Portal — Reporting (M10)");

        reports.MapGet("/worklist", (ISender sender, CancellationToken ct) =>
            sender.Send(new GetReportingWorklistQuery(), ct));

        reports.MapGet("/{reportId:guid}/content", async (ISender sender, Guid reportId, CancellationToken ct) =>
        {
            var artifact = await sender.Send(new GetReportArtifactQuery(reportId), ct);
            return Results.Content(artifact.ContentHtml, "text/html; charset=utf-8");
        });

        reports.MapPost("/{reportId:guid}/deliver", (
            ISender sender, Guid reportId, DeliverReportRequest request, CancellationToken ct) =>
            sender.Send(new DeliverReportCommand(reportId, request.Channel, request.Destination), ct));

        group.MapPost("/visits/{visitId:guid}/reports", async (
            ISender sender, Guid visitId, RenderReportRequest request, CancellationToken ct) =>
        {
            var dto = await sender.Send(new RenderReportCommand(visitId, request.Kind), ct);
            return Results.Created($"/api/v1/reports/{dto.ReportId}", dto);
        }).RequireAuthorization().WithTags("Client Portal — Reporting (M10)");

        // P10.2 — public QR verification: anonymous, PHI-free, cross-tenant by design.
        group.MapGet("/public/reports/{reportId:guid}/verify", (
            ISender sender, Guid reportId, string hash, CancellationToken ct) =>
            sender.Send(new VerifyReportQuery(reportId, hash), ct))
            .AllowAnonymous().WithTags("Public — Report Verification");

        return group;
    }
}
