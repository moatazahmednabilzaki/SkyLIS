using MediatR;
using SkyLIS.Application.Results;

namespace SkyLIS.Api.Endpoints;

public static class ResultEndpoints
{
    public sealed record EnterResultRequest(Guid VisitTestId, decimal Value);
    public sealed record RerunRequest(string Reason);
    public sealed record ValidateMedicalRequest(string? InterpretiveComment, string SignatureIntent);
    public sealed record CriticalCallRequest(string CalledPerson, string Phone, bool ReadBackConfirmed);

    public static RouteGroupBuilder MapResultEndpoints(this RouteGroupBuilder group)
    {
        var results = group.MapGroup("/results").RequireAuthorization().WithTags("Client Portal — Results (M09)");

        // M23 executive dashboard (P23.1)
        group.MapGet("/analytics/dashboard", (ISender sender, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Analytics.GetDashboardQuery(), ct))
            .RequireAuthorization().WithTags("Client Portal — Analytics (M23)");

        // FR-SYS-001 audit trail explorer + tamper-evidence verification
        var audit = group.MapGroup("/audit").RequireAuthorization().WithTags("Client Portal — Audit Trail");
        audit.MapGet("/events", (ISender sender, string? entityType, string? entityId, int? take, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Audit.SearchAuditQuery(entityType, entityId, take ?? 50), ct));
        audit.MapGet("/verify-chain", (ISender sender, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Audit.VerifyAuditChainQuery(), ct));

        // Worklists
        results.MapGet("/pending-entry", (ISender sender, CancellationToken ct) =>
            sender.Send(new GetPendingEntryQuery(), ct));
        results.MapGet("/technical-queue", (ISender sender, CancellationToken ct) =>
            sender.Send(new GetTechnicalQueueQuery(), ct));
        results.MapGet("/medical-queue", (ISender sender, CancellationToken ct) =>
            sender.Send(new GetMedicalQueueQuery(), ct));
        results.MapGet("/critical-queue", (ISender sender, CancellationToken ct) =>
            sender.Send(new GetCriticalQueueQuery(), ct));

        // P09.1 — entry (on the visit resource, mirroring the clinical action)
        group.MapPost("/visits/{visitId:guid}/results", async (
            ISender sender, Guid visitId, EnterResultRequest request, CancellationToken ct) =>
        {
            var dto = await sender.Send(new EnterResultCommand(visitId, request.VisitTestId, request.Value), ct);
            return Results.Created($"/api/v1/results/{dto.ResultId}", dto);
        }).RequireAuthorization().WithTags("Client Portal — Results (M09)");

        // Explicit business-action endpoints (EAA API rule)
        results.MapPost("/{resultId:guid}/accept-technical", async (ISender sender, Guid resultId, CancellationToken ct) =>
        {
            await sender.Send(new AcceptTechnicalCommand(resultId), ct);
            return Results.NoContent();
        });

        results.MapPost("/{resultId:guid}/rerun", async (
            ISender sender, Guid resultId, RerunRequest request, CancellationToken ct) =>
        {
            await sender.Send(new OrderRerunCommand(resultId, request.Reason), ct);
            return Results.NoContent();
        });

        results.MapPost("/{resultId:guid}/validate-medical", async (
            ISender sender, Guid resultId, ValidateMedicalRequest request, CancellationToken ct) =>
        {
            await sender.Send(new ValidateMedicalCommand(resultId, request.InterpretiveComment, request.SignatureIntent), ct);
            return Results.NoContent();
        });

        results.MapPost("/{resultId:guid}/critical/document-call", async (
            ISender sender, Guid resultId, CriticalCallRequest request, CancellationToken ct) =>
        {
            await sender.Send(new DocumentCriticalCallCommand(
                resultId, request.CalledPerson, request.Phone, request.ReadBackConfirmed), ct);
            return Results.NoContent();
        });

        return group;
    }
}
