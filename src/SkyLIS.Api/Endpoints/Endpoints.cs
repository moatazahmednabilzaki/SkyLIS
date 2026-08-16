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
        string LegalName, string Subdomain, string CountryCode, string PlanCode, IsolationTier IsolationTier,
        string AdminUserName, string AdminFullName, string AdminPassword);
    public sealed record SuspendTenantRequest(string Reason);
    public sealed record ChangePlanRequest(string PlanCode);

    public static RouteGroupBuilder MapTenantEndpoints(this RouteGroupBuilder group)
    {
        var tenants = group.MapGroup("/platform/tenants").RequireAuthorization().WithTags("Admin Portal — Tenants");

        tenants.MapGet("/", (ISender sender, string? search, CancellationToken ct) =>
            sender.Send(new GetTenantsQuery(search), ct));

        tenants.MapPost("/", async (ISender sender, ProvisionTenantRequest request, CancellationToken ct) =>
        {
            var id = await sender.Send(new ProvisionTenantCommand(
                request.LegalName, request.Subdomain, request.CountryCode,
                request.PlanCode, request.IsolationTier,
                request.AdminUserName, request.AdminFullName, request.AdminPassword), ct);
            return Results.Created($"/api/v1/platform/tenants/{id}", new { id });
        });

        // P01.3 metering explorer: monthly finalized-report counters (FR-SYS-011)
        tenants.MapGet("/{tenantId:guid}/usage", (ISender sender, Guid tenantId, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Platform.GetTenantUsageQuery(tenantId), ct));

        // P01.1 tenant lifecycle (guarded state machine; suspended tenants cannot sign in)
        tenants.MapPost("/{tenantId:guid}/activate", async (ISender sender, Guid tenantId, CancellationToken ct) =>
        {
            await sender.Send(new ActivateTenantCommand(tenantId), ct);
            return Results.NoContent();
        });
        tenants.MapPost("/{tenantId:guid}/suspend", async (
            ISender sender, Guid tenantId, SuspendTenantRequest request, CancellationToken ct) =>
        {
            await sender.Send(new SuspendTenantCommand(tenantId, request.Reason), ct);
            return Results.NoContent();
        });
        tenants.MapPost("/{tenantId:guid}/offboard", async (ISender sender, Guid tenantId, CancellationToken ct) =>
        {
            await sender.Send(new OffboardTenantCommand(tenantId), ct);
            return Results.NoContent();
        });

        // P01.3: move the tenant to another plan (entitlements apply immediately)
        tenants.MapPost("/{tenantId:guid}/change-plan", async (
            ISender sender, Guid tenantId, ChangePlanRequest request, CancellationToken ct) =>
        {
            await sender.Send(new SkyLIS.Application.Platform.ChangeTenantPlanCommand(tenantId, request.PlanCode), ct);
            return Results.NoContent();
        });

        // P01.5: read-only monitor of the tenant's user accounts (identity metadata, no PHI)
        tenants.MapGet("/{tenantId:guid}/users", (ISender sender, Guid tenantId, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Platform.GetTenantUsersQuery(tenantId), ct));

        // P01.3 plan builder
        var plans = group.MapGroup("/platform/plans").RequireAuthorization()
            .WithTags("Admin Portal — Plans (P01.3)");

        plans.MapGet("/", (ISender sender, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Platform.ListPlansQuery(), ct));

        plans.MapPut("/", async (
            ISender sender, SkyLIS.Application.Platform.UpsertPlanCommand command, CancellationToken ct) =>
        {
            var id = await sender.Send(command, ct);
            return Results.Ok(new { id });
        });

        // P01.6 platform health: outbox dispatch status
        group.MapGet("/platform/outbox/status", (ISender sender, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Platform.GetOutboxStatusQuery(), ct))
            .RequireAuthorization().WithTags("Admin Portal — Platform Health");

        // P01.4 country default packs (seed new tenants at provisioning, FR-TEN-040)
        var packs = group.MapGroup("/platform/country-packs").RequireAuthorization()
            .WithTags("Admin Portal — Country Packs");

        packs.MapGet("/", (ISender sender, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Platform.ListCountryPacksQuery(), ct));

        packs.MapPut("/", async (
            ISender sender, SkyLIS.Application.Platform.UpsertCountryPackCommand command, CancellationToken ct) =>
        {
            var id = await sender.Send(command, ct);
            return Results.Ok(new { id });
        });

        // P01.7 master data packs: platform test catalogue with push-to-all-tenants
        var masterTests = group.MapGroup("/platform/master-tests").RequireAuthorization()
            .WithTags("Admin Portal — Master Data Packs (P01.7)");

        masterTests.MapGet("/", (ISender sender, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Platform.ListMasterTestsQuery(), ct));

        masterTests.MapPost("/", async (
            ISender sender, SkyLIS.Application.Platform.CreateMasterTestCommand command, CancellationToken ct) =>
        {
            var id = await sender.Send(command, ct);
            return Results.Created($"/api/v1/platform/master-tests/{id}", new { id });
        });

        masterTests.MapPost("/{masterTestId:guid}/push", async (
            ISender sender, Guid masterTestId, CancellationToken ct) =>
        {
            var targetCount = await sender.Send(
                new SkyLIS.Application.Platform.PushMasterTestCommand(masterTestId), ct);
            return Results.Ok(new { targetCount });
        });

        return group;
    }
}

public static class PlatformServiceEndpoints
{
    public sealed record UploadAttachmentRequest(
        string EntityType, Guid EntityId, string FileName, string ContentType, string ContentBase64);

    public static RouteGroupBuilder MapPlatformServiceEndpoints(this RouteGroupBuilder group)
    {
        // FR-SYS-008 global search (Ctrl+K)
        group.MapGet("/search", (ISender sender, string term, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Search.GlobalSearchQuery(term), ct))
            .RequireAuthorization().WithTags("Client Portal — Global Search (FR-SYS-008)");

        // FR-SYS-007 attachments
        var attachments = group.MapGroup("/attachments").RequireAuthorization()
            .WithTags("Client Portal — Attachments (FR-SYS-007)");

        attachments.MapGet("/", (ISender sender, string entityType, Guid entityId, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Files.ListAttachmentsQuery(entityType, entityId), ct));

        attachments.MapPost("/", async (ISender sender, UploadAttachmentRequest request, CancellationToken ct) =>
        {
            var dto = await sender.Send(new SkyLIS.Application.Files.UploadAttachmentCommand(
                request.EntityType, request.EntityId, request.FileName, request.ContentType, request.ContentBase64), ct);
            return Results.Created($"/api/v1/attachments/{dto.Id}", dto);
        });

        attachments.MapGet("/{attachmentId:guid}/content", async (
            ISender sender, Guid attachmentId, CancellationToken ct) =>
        {
            var content = await sender.Send(
                new SkyLIS.Application.Files.GetAttachmentContentQuery(attachmentId), ct);
            return Results.File(content.Content, content.ContentType, content.FileName);
        });

        return group;
    }
}

public static class OrgEndpoints
{
    public sealed record CreateBranchRequest(string Code, string Name, string? Address, string? Phone);
    public sealed record AddDepartmentRequest(string Code, string Name);
    public sealed record SetBranchActiveRequest(bool IsActive);
    public sealed record SetSettingRequest(string Key, string Value);

    public static RouteGroupBuilder MapOrgEndpoints(this RouteGroupBuilder group)
    {
        var branches = group.MapGroup("/org/branches").RequireAuthorization()
            .WithTags("Client Portal — Branches & Departments (P03.2)");

        branches.MapGet("/", (ISender sender, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Org.ListBranchesQuery(), ct));

        branches.MapPost("/", async (ISender sender, CreateBranchRequest request, CancellationToken ct) =>
        {
            var id = await sender.Send(new SkyLIS.Application.Org.CreateBranchCommand(
                request.Code, request.Name, request.Address, request.Phone), ct);
            return Results.Created($"/api/v1/org/branches/{id}", new { id });
        });

        branches.MapPost("/{branchId:guid}/departments", async (
            ISender sender, Guid branchId, AddDepartmentRequest request, CancellationToken ct) =>
        {
            var id = await sender.Send(new SkyLIS.Application.Org.AddDepartmentCommand(
                branchId, request.Code, request.Name), ct);
            return Results.Created($"/api/v1/org/branches/{branchId}/departments/{id}", new { id });
        });

        branches.MapPost("/{branchId:guid}/set-active", async (
            ISender sender, Guid branchId, SetBranchActiveRequest request, CancellationToken ct) =>
        {
            await sender.Send(new SkyLIS.Application.Org.SetBranchActiveCommand(branchId, request.IsActive), ct);
            return Results.NoContent();
        });

        // FR-SYS-004: tenant configuration (report branding, rejection vocabulary, …)
        var settings = group.MapGroup("/org/settings").RequireAuthorization()
            .WithTags("Client Portal — Settings (FR-SYS-004)");

        settings.MapGet("/", (ISender sender, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Org.ListTenantSettingsQuery(), ct));

        settings.MapPut("/", async (ISender sender, SetSettingRequest request, CancellationToken ct) =>
        {
            await sender.Send(new SkyLIS.Application.Org.SetTenantSettingCommand(request.Key, request.Value), ct);
            return Results.NoContent();
        });

        // P03.1: guided setup checklist
        group.MapGet("/org/setup-status", (ISender sender, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Org.GetSetupStatusQuery(), ct))
            .RequireAuthorization().WithTags("Client Portal — Setup Wizard (P03.1)");

        return group;
    }
}

public static class PatientEndpoints
{
    public sealed record RegisterPatientRequest(
        string FullName, SkyLIS.Domain.Patients.Sex Sex, DateOnly DateOfBirth, string Mobile, string? NationalId);
    public sealed record MergePatientsRequest(Guid SurvivorId, Guid DuplicateId, string Reason);
    public sealed record DataSubjectReasonRequest(string Reason);

    public static RouteGroupBuilder MapPatientEndpoints(this RouteGroupBuilder group)
    {
        var patients = group.MapGroup("/patients").RequireAuthorization().WithTags("Client Portal — Patients");

        patients.MapGet("/search", (ISender sender, string term, CancellationToken ct) =>
            sender.Send(new SearchPatientsQuery(term), ct));

        // P04.3 Patient 360: full story — demographics, visits, money, reports
        patients.MapGet("/{patientId:guid}/summary", (ISender sender, Guid patientId, CancellationToken ct) =>
            sender.Send(new GetPatient360Query(patientId), ct));

        // P10.3 cumulative trend for one test on this patient
        patients.MapGet("/{patientId:guid}/results/cumulative", (
            ISender sender, Guid patientId, string testCode, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Results.GetCumulativeQuery(patientId, testCode), ct));

        // P04.4 duplicate merge console
        patients.MapGet("/duplicates", (ISender sender, CancellationToken ct) =>
            sender.Send(new FindDuplicatesQuery(), ct));

        patients.MapPost("/merge", async (ISender sender, MergePatientsRequest request, CancellationToken ct) =>
        {
            var moved = await sender.Send(new MergePatientsCommand(
                request.SurvivorId, request.DuplicateId, request.Reason), ct);
            return Results.Ok(new { movedArtifacts = moved });
        });

        // P04.5 data-subject requests
        patients.MapGet("/data-subject-requests", (ISender sender, CancellationToken ct) =>
            sender.Send(new ListDataSubjectRequestsQuery(), ct));

        patients.MapPost("/{patientId:guid}/export", (
            ISender sender, Guid patientId, DataSubjectReasonRequest request, CancellationToken ct) =>
            sender.Send(new ExportPatientDataCommand(patientId, request.Reason), ct));

        patients.MapPost("/{patientId:guid}/erasure-requests", async (
            ISender sender, Guid patientId, DataSubjectReasonRequest request, CancellationToken ct) =>
        {
            var id = await sender.Send(new RequestErasureCommand(patientId, request.Reason), ct);
            return Results.Created($"/api/v1/patients/data-subject-requests/{id}", new { id });
        });

        patients.MapPost("/erasure-requests/{requestId:guid}/approve", async (
            ISender sender, Guid requestId, CancellationToken ct) =>
        {
            await sender.Send(new ApproveErasureCommand(requestId), ct);
            return Results.NoContent();
        });

        patients.MapPost("/", async (ISender sender, RegisterPatientRequest request, CancellationToken ct) =>
        {
            var id = await sender.Send(new RegisterPatientCommand(
                request.FullName, request.Sex, request.DateOfBirth, request.Mobile, request.NationalId), ct);
            return Results.Created($"/api/v1/patients/{id}", new { id });
        });

        return group;
    }
}

public static class UserEndpoints
{
    public sealed record CreateUserRequest(string UserName, string FullName, string Password, List<string> Roles);
    public sealed record SetUserStatusRequest(string Action);
    public sealed record ResetPasswordRequest(string NewPassword);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        var users = group.MapGroup("/users").RequireAuthorization().WithTags("Client Portal — Users & Roles (M02)");

        users.MapGet("/", (ISender sender, CancellationToken ct) =>
            sender.Send(new SkyLIS.Application.Users.ListUsersQuery(), ct));

        // P02.1: lock / unlock / deactivate (admins cannot act on themselves)
        users.MapPost("/{userId:guid}/set-status", async (
            ISender sender, Guid userId, SetUserStatusRequest request, CancellationToken ct) =>
        {
            await sender.Send(new SkyLIS.Application.Users.SetUserStatusCommand(userId, request.Action), ct);
            return Results.NoContent();
        });

        // Support flow: admin resets a user's password
        users.MapPost("/{userId:guid}/reset-password", async (
            ISender sender, Guid userId, ResetPasswordRequest request, CancellationToken ct) =>
        {
            await sender.Send(new SkyLIS.Application.Users.ResetPasswordCommand(userId, request.NewPassword), ct);
            return Results.NoContent();
        });

        // §4.3: self-service password change (current password re-verified)
        users.MapPost("/me/change-password", async (
            ISender sender, ChangePasswordRequest request, CancellationToken ct) =>
        {
            await sender.Send(new SkyLIS.Application.Users.ChangePasswordCommand(
                request.CurrentPassword, request.NewPassword), ct);
            return Results.NoContent();
        });

        users.MapPost("/", async (ISender sender, CreateUserRequest request, CancellationToken ct) =>
        {
            var id = await sender.Send(new SkyLIS.Application.Users.CreateUserCommand(
                request.UserName, request.FullName, request.Password, request.Roles), ct);
            return Results.Created($"/api/v1/users/{id}", new { id });
        });

        users.MapGet("/roles", () => Results.Ok(
            SkyLIS.Domain.Users.RoleCatalog.AllRoles
                .Select(r => new { role = r, permissions = SkyLIS.Domain.Users.RoleCatalog.PermissionsOf(r) })));

        return group;
    }
}

public static class CatalogEndpoints
{
    public sealed record CreateTenantTestRequest(
        string Code, string Name, string Department, Guid SampleTypeId,
        Guid? RequiredConditionId, decimal Price, string Currency);

    public sealed record ActivatePushedTestRequest(decimal Price, string Currency);
    public sealed record CreatePanelRequest(
        string Code, string Name, decimal Price, string Currency, List<Guid> TestIds);
    public sealed record ImportTestsRequest(string Csv);
    public sealed record CreateSampleTypeRequest(string Name, string ContainerName, List<ConditionInput> Conditions);
    public sealed record ResultSchemaRequest(
        string Unit, decimal? RefLow, decimal? RefHigh, decimal? CriticalLow, decimal? CriticalHigh,
        decimal? AbsurdLow, decimal? AbsurdHigh, bool AutoVerify, decimal? DeltaThresholdPercent);

    public static RouteGroupBuilder MapCatalogEndpoints(this RouteGroupBuilder group)
    {
        var sampleTypes = group.MapGroup("/catalog/sample-types").RequireAuthorization().WithTags("Client Portal — Catalog");

        sampleTypes.MapGet("/", (ISender sender, CancellationToken ct) =>
            sender.Send(new ListSampleTypesQuery(), ct));

        sampleTypes.MapPost("/", async (ISender sender, CreateSampleTypeRequest request, CancellationToken ct) =>
        {
            var dto = await sender.Send(new CreateSampleTypeCommand(
                request.Name, request.ContainerName, request.Conditions), ct);
            return Results.Created($"/api/v1/catalog/sample-types/{dto.Id}", dto);
        });

        // P03.5 panels/profiles: bundles at a bundle price
        var panels = group.MapGroup("/catalog/panels").RequireAuthorization().WithTags("Client Portal — Catalog");

        panels.MapGet("/", (ISender sender, CancellationToken ct) =>
            sender.Send(new ListPanelsQuery(), ct));

        panels.MapPost("/", async (ISender sender, CreatePanelRequest request, CancellationToken ct) =>
        {
            var id = await sender.Send(new CreatePanelCommand(
                request.Code, request.Name, request.Price, request.Currency, request.TestIds), ct);
            return Results.Created($"/api/v1/catalog/panels/{id}", new { id });
        });

        var catalog = group.MapGroup("/catalog/tests").RequireAuthorization().WithTags("Client Portal — Catalog");

        catalog.MapGet("/", (ISender sender, string? status, CancellationToken ct) =>
            sender.Send(new ListTestsQuery(status), ct));

        // FR-SYS-009: CSV round-trip (export header == import header)
        catalog.MapGet("/export.csv", async (ISender sender, CancellationToken ct) =>
        {
            var csv = await sender.Send(new ExportTestsQuery(), ct);
            return Results.Text(csv, "text/csv", System.Text.Encoding.UTF8);
        });

        catalog.MapPost("/import", (ISender sender, ImportTestsRequest request, CancellationToken ct) =>
            sender.Send(new ImportTestsCommand(request.Csv), ct));

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
    public sealed record RegisterVisitRequest(
        Guid PatientId, Guid BranchId, IReadOnlyList<Guid> TestIds, bool IsStat, string? StatReason,
        IReadOnlyList<Guid>? PanelIds = null);
    public sealed record AddTestsRequest(IReadOnlyList<Guid> TestIds);
    public sealed record RejectSampleRequest(string ReasonCode);
    public sealed record CancelVisitRequest(string Reason);

    public static RouteGroupBuilder MapVisitEndpoints(this RouteGroupBuilder group)
    {
        var visits = group.MapGroup("/visits").RequireAuthorization().WithTags("Client Portal — Visits");

        visits.MapPost("/", async (ISender sender, RegisterVisitRequest request, CancellationToken ct) =>
        {
            var result = await sender.Send(new RegisterVisitCommand(
                request.PatientId, request.BranchId, request.TestIds, request.IsStat, request.StatReason,
                request.PanelIds), ct);
            return Results.Created($"/api/v1/visits/{result.VisitId}", result);
        });

        visits.MapGet("/{visitId:guid}", (ISender sender, Guid visitId, CancellationToken ct) =>
            sender.Send(new GetVisitQuery(visitId), ct));

        // P05.4: add tests to an open visit (new samples + supplementary invoice)
        visits.MapPost("/{visitId:guid}/add-tests", (
            ISender sender, Guid visitId, AddTestsRequest request, CancellationToken ct) =>
            sender.Send(new AddTestsToVisitCommand(visitId, request.TestIds), ct));

        // M05/M17: cancellation waives the unpaid balance via an automatic credit note
        visits.MapPost("/{visitId:guid}/cancel", (
            ISender sender, Guid visitId, CancelVisitRequest request, CancellationToken ct) =>
            sender.Send(new CancelVisitCommand(visitId, request.Reason), ct));

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
    public sealed record ApplyDiscountRequest(decimal Amount, string Reason);
    public sealed record IssueCreditNoteRequest(decimal Amount, string Reason);
    public sealed record RefundRequest(decimal Amount, string Reason);
    public sealed record OpenShiftRequest(Guid BranchId, decimal OpeningFloat, string Currency);
    public sealed record CloseShiftRequest(decimal DeclaredCash);

    public static RouteGroupBuilder MapBillingEndpoints(this RouteGroupBuilder group)
    {
        var billing = group.MapGroup("/billing/invoices").RequireAuthorization().WithTags("Client Portal — Billing");

        billing.MapGet("/{invoiceId:guid}", (ISender sender, Guid invoiceId, CancellationToken ct) =>
            sender.Send(new GetInvoiceQuery(invoiceId), ct));

        billing.MapPost("/{invoiceId:guid}/payments", (
            ISender sender, Guid invoiceId, CapturePaymentRequest request, CancellationToken ct) =>
            sender.Send(new CapturePaymentCommand(invoiceId, request.Amount, request.Currency, request.Method), ct));

        // P17.1: discount before payment (reason mandatory, audited)
        billing.MapPost("/{invoiceId:guid}/discount", (
            ISender sender, Guid invoiceId, ApplyDiscountRequest request, CancellationToken ct) =>
            sender.Send(new ApplyDiscountCommand(invoiceId, request.Amount, request.Reason), ct));

        // M17: manual credit note against the open balance
        billing.MapPost("/{invoiceId:guid}/credit-notes", (
            ISender sender, Guid invoiceId, IssueCreditNoteRequest request, CancellationToken ct) =>
            sender.Send(new IssueCreditNoteCommand(invoiceId, request.Amount, request.Reason), ct));

        // M17: refund captured money (SoD: billing.refund.approve)
        billing.MapPost("/{invoiceId:guid}/refunds", (
            ISender sender, Guid invoiceId, RefundRequest request, CancellationToken ct) =>
            sender.Send(new RefundPaymentCommand(invoiceId, request.Amount, request.Reason), ct));

        // P17.2: cashier shifts & day close (Z-report)
        var shifts = group.MapGroup("/billing/shifts").RequireAuthorization()
            .WithTags("Client Portal — Cashier Shifts (P17.2)");

        shifts.MapGet("/", (ISender sender, CancellationToken ct) =>
            sender.Send(new ListShiftsQuery(), ct));

        shifts.MapPost("/", async (ISender sender, OpenShiftRequest request, CancellationToken ct) =>
        {
            var id = await sender.Send(new OpenShiftCommand(
                request.BranchId, request.OpeningFloat, request.Currency), ct);
            return Results.Created($"/api/v1/billing/shifts/{id}", new { id });
        });

        shifts.MapPost("/{shiftId:guid}/close", (
            ISender sender, Guid shiftId, CloseShiftRequest request, CancellationToken ct) =>
            sender.Send(new CloseShiftCommand(shiftId, request.DeclaredCash), ct));

        return group;
    }
}
