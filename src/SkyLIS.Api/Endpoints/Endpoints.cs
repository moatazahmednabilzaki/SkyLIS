using MediatR;
using SkyLIS.Application.Billing;
using SkyLIS.Application.Catalog;
using SkyLIS.Application.Patients;
using SkyLIS.Application.Tenants;
using SkyLIS.Application.Visits;
using SkyLIS.Domain.Tenants;

namespace SkyLIS.Api.Endpoints;

// Thin endpoints: bind request DTO → dispatch → shape response. No business logic here
// (EAA rule). Authorization/tenancy/validation run in the MediatR pipeline behaviors.

public static class TenantEndpoints
{
    public sealed record ProvisionTenantRequest(
        string LegalName, string Subdomain, string CountryCode, string PlanCode, IsolationTier IsolationTier);

    public static RouteGroupBuilder MapTenantEndpoints(this RouteGroupBuilder group)
    {
        var tenants = group.MapGroup("/platform/tenants").RequireAuthorization().WithTags("Admin Portal — Tenants");

        tenants.MapGet("/", (ISender sender, string? search, CancellationToken ct) =>
            sender.Send(new GetTenantsQuery(search), ct));

        tenants.MapPost("/", async (ISender sender, ProvisionTenantRequest request, CancellationToken ct) =>
        {
            var id = await sender.Send(new ProvisionTenantCommand(
                request.LegalName, request.Subdomain, request.CountryCode,
                request.PlanCode, request.IsolationTier), ct);
            return Results.Created($"/api/v1/platform/tenants/{id}", new { id });
        });

        return group;
    }
}

public static class PatientEndpoints
{
    public sealed record RegisterPatientRequest(
        string FullName, SkyLIS.Domain.Patients.Sex Sex, DateOnly DateOfBirth, string Mobile, string? NationalId);

    public static RouteGroupBuilder MapPatientEndpoints(this RouteGroupBuilder group)
    {
        var patients = group.MapGroup("/patients").RequireAuthorization().WithTags("Client Portal — Patients");

        patients.MapGet("/search", (ISender sender, string term, CancellationToken ct) =>
            sender.Send(new SearchPatientsQuery(term), ct));

        patients.MapPost("/", async (ISender sender, RegisterPatientRequest request, CancellationToken ct) =>
        {
            var id = await sender.Send(new RegisterPatientCommand(
                request.FullName, request.Sex, request.DateOfBirth, request.Mobile, request.NationalId), ct);
            return Results.Created($"/api/v1/patients/{id}", new { id });
        });

        return group;
    }
}

public static class CatalogEndpoints
{
    public sealed record CreateTenantTestRequest(
        string Code, string Name, string Department, Guid SampleTypeId,
        Guid? RequiredConditionId, decimal Price, string Currency);

    public sealed record ActivatePushedTestRequest(decimal Price, string Currency);
    public sealed record CreateSampleTypeRequest(string Name, string ContainerName, List<ConditionInput> Conditions);
    public sealed record ResultSchemaRequest(
        string Unit, decimal? RefLow, decimal? RefHigh, decimal? CriticalLow, decimal? CriticalHigh,
        decimal? AbsurdLow, decimal? AbsurdHigh, bool AutoVerify, decimal? DeltaThresholdPercent);

    public static RouteGroupBuilder MapCatalogEndpoints(this RouteGroupBuilder group)
    {
        var sampleTypes = group.MapGroup("/catalog/sample-types").RequireAuthorization().WithTags("Client Portal — Catalog");

        sampleTypes.MapPost("/", async (ISender sender, CreateSampleTypeRequest request, CancellationToken ct) =>
        {
            var dto = await sender.Send(new CreateSampleTypeCommand(
                request.Name, request.ContainerName, request.Conditions), ct);
            return Results.Created($"/api/v1/catalog/sample-types/{dto.Id}", dto);
        });

        var catalog = group.MapGroup("/catalog/tests").RequireAuthorization().WithTags("Client Portal — Catalog");

        catalog.MapPost("/{testId:guid}/submit", async (ISender sender, Guid testId, CancellationToken ct) =>
        {
            await sender.Send(new SubmitTestForReviewCommand(testId), ct);
            return Results.NoContent();
        });

        catalog.MapPost("/{testId:guid}/approve", async (ISender sender, Guid testId, CancellationToken ct) =>
        {
            await sender.Send(new ApproveTestCommand(testId), ct);
            return Results.NoContent();
        });

        catalog.MapPut("/{testId:guid}/result-schema", async (
            ISender sender, Guid testId, ResultSchemaRequest request, CancellationToken ct) =>
        {
            await sender.Send(new SetResultSchemaCommand(
                testId, request.Unit, request.RefLow, request.RefHigh, request.CriticalLow, request.CriticalHigh,
                request.AbsurdLow, request.AbsurdHigh, request.AutoVerify, request.DeltaThresholdPercent), ct);
            return Results.NoContent();
        });

        catalog.MapPost("/", async (ISender sender, CreateTenantTestRequest request, CancellationToken ct) =>
        {
            var id = await sender.Send(new CreateTenantTestCommand(
                request.Code, request.Name, request.Department, request.SampleTypeId,
                request.RequiredConditionId, request.Price, request.Currency), ct);
            return Results.Created($"/api/v1/catalog/tests/{id}", new { id });
        });

        catalog.MapPost("/{testId:guid}/activate", async (
            ISender sender, Guid testId, ActivatePushedTestRequest request, CancellationToken ct) =>
        {
            await sender.Send(new ActivatePushedTestCommand(testId, request.Price, request.Currency), ct);
            return Results.NoContent();
        });

        return group;
    }
}

public static class VisitEndpoints
{
    public sealed record RegisterVisitRequest(Guid PatientId, IReadOnlyList<Guid> TestIds, bool IsStat, string? StatReason);
    public sealed record RejectSampleRequest(string ReasonCode);

    public static RouteGroupBuilder MapVisitEndpoints(this RouteGroupBuilder group)
    {
        var visits = group.MapGroup("/visits").RequireAuthorization().WithTags("Client Portal — Visits");

        visits.MapPost("/", async (ISender sender, RegisterVisitRequest request, CancellationToken ct) =>
        {
            var result = await sender.Send(new RegisterVisitCommand(
                request.PatientId, request.TestIds, request.IsStat, request.StatReason), ct);
            return Results.Created($"/api/v1/visits/{result.VisitId}", result);
        });

        visits.MapGet("/{visitId:guid}", (ISender sender, Guid visitId, CancellationToken ct) =>
            sender.Send(new GetVisitQuery(visitId), ct));

        // Explicit business-action endpoints (EAA API rule)
        visits.MapPost("/{visitId:guid}/samples/{sampleId:guid}/collect", async (
            ISender sender, Guid visitId, Guid sampleId, CancellationToken ct) =>
        {
            await sender.Send(new CollectSampleCommand(visitId, sampleId), ct);
            return Results.NoContent();
        });

        visits.MapPost("/{visitId:guid}/samples/{sampleId:guid}/receive", async (
            ISender sender, Guid visitId, Guid sampleId, CancellationToken ct) =>
        {
            await sender.Send(new ReceiveSampleCommand(visitId, sampleId), ct);
            return Results.NoContent();
        });

        visits.MapPost("/{visitId:guid}/samples/{sampleId:guid}/reject", async (
            ISender sender, Guid visitId, Guid sampleId, RejectSampleRequest request, CancellationToken ct) =>
        {
            var recollectionId = await sender.Send(new RejectSampleCommand(visitId, sampleId, request.ReasonCode), ct);
            return Results.Ok(new { recollectionSampleId = recollectionId });
        });

        return group;
    }
}

public static class BillingEndpoints
{
    public sealed record CapturePaymentRequest(decimal Amount, string Currency, string Method);

    public static RouteGroupBuilder MapBillingEndpoints(this RouteGroupBuilder group)
    {
        var billing = group.MapGroup("/billing/invoices").RequireAuthorization().WithTags("Client Portal — Billing");

        billing.MapPost("/{invoiceId:guid}/payments", (
            ISender sender, Guid invoiceId, CapturePaymentRequest request, CancellationToken ct) =>
            sender.Send(new CapturePaymentCommand(invoiceId, request.Amount, request.Currency, request.Method), ct));

        return group;
    }
}
