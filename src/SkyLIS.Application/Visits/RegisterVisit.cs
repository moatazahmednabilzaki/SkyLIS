using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Billing;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Visits;

namespace SkyLIS.Application.Visits;

public sealed record RegisteredVisitDto(
    Guid VisitId, string VisitNumber, Guid InvoiceId, string InvoiceNumber,
    decimal Total, string Currency, IReadOnlyList<RegisteredSampleDto> Samples);

public sealed record RegisteredSampleDto(
    Guid SampleId, string Barcode, string State, string? Condition, DateTimeOffset? ReadyAtUtc);

/// <summary>
/// FR-ORD-010: visit registration — the highest-traffic use case. Computes the specimen
/// plan (condition consolidation + reservation), registers the visit, and issues the invoice
/// in one aggregate-consistent transaction per aggregate (single SaveChanges, outbox events).
/// </summary>
public sealed record RegisterVisitCommand(
    Guid PatientId,
    IReadOnlyList<Guid> TestIds,
    bool IsStat,
    string? StatReason) : ICommand<RegisteredVisitDto>, IRequirePermission
{
    public string Permission => "orders.visit.create";
}

internal sealed class RegisterVisitValidator : AbstractValidator<RegisterVisitCommand>
{
    public RegisterVisitValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.TestIds).NotEmpty().WithMessage("A visit shall not be registered with zero tests.");
        RuleFor(x => x.StatReason).NotEmpty().When(x => x.IsStat)
            .WithMessage("STAT priority requires a reason.");
    }
}

internal sealed class RegisterVisitHandler : IRequestHandler<RegisterVisitCommand, RegisteredVisitDto>
{
    private readonly IPatientRepository _patients;
    private readonly ILabTestRepository _tests;
    private readonly ISampleTypeRepository _sampleTypes;
    private readonly IVisitRepository _visits;
    private readonly IInvoiceRepository _invoices;
    private readonly INumberSeriesService _numbers;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public RegisterVisitHandler(
        IPatientRepository patients, ILabTestRepository tests, ISampleTypeRepository sampleTypes,
        IVisitRepository visits, IInvoiceRepository invoices, INumberSeriesService numbers,
        ITenantContext tenant, IClock clock)
    {
        _patients = patients;
        _tests = tests;
        _sampleTypes = sampleTypes;
        _visits = visits;
        _invoices = invoices;
        _numbers = numbers;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<RegisteredVisitDto> Handle(RegisterVisitCommand request, CancellationToken ct)
    {
        var patient = await _patients.GetAsync(request.PatientId, ct)
            ?? throw new NotFoundException("Patient", request.PatientId);

        var tests = await _tests.GetManyAsync(request.TestIds.Distinct().ToArray(), ct);
        var missing = request.TestIds.Except(tests.Select(t => t.Id)).ToList();
        if (missing.Count > 0)
            throw new NotFoundException("LabTest", string.Join(", ", missing));

        var conditionIds = tests.Where(t => t.RequiredConditionId is not null)
                                .Select(t => t.RequiredConditionId!.Value).Distinct().ToArray();
        var conditions = await _sampleTypes.GetConditionsAsync(conditionIds, ct);
        var conditionById = conditions.ToDictionary(c => c.Id);

        var inputs = tests.Select(t => new SpecimenPlanner.PlanInput(
            t, t.RequiredConditionId is null ? null : conditionById[t.RequiredConditionId.Value])).ToList();

        // Number series commit independently (gap-tolerant); both are acquired BEFORE any
        // aggregate is tracked so the single SaveChanges at the end stays atomic.
        var visitNumber = await _numbers.NextAsync("visit", ct);
        var invoiceNumber = await _numbers.NextAsync("invoice", ct);

        var barcodeIndex = 0;
        var plan = SpecimenPlanner.Compute(
            inputs,
            Guid.CreateVersion7,
            _ => $"{visitNumber}-S{++barcodeIndex}");

        var now = _clock.UtcNow;
        var visit = Visit.Register(
            Guid.CreateVersion7(), _tenant.TenantId, visitNumber, patient.Id,
            plan.Tests, plan.Samples, request.IsStat, request.StatReason, now);
        patient.RecordVisit(now);
        _visits.Add(visit);

        var currency = tests[0].Price!.Currency;
        var invoice = Invoice.IssueForVisit(
            Guid.CreateVersion7(), _tenant.TenantId, invoiceNumber, visit.Id, visit.Total(currency), now);
        _invoices.Add(invoice);

        return new RegisteredVisitDto(
            visit.Id, visit.VisitNumber, invoice.Id, invoice.InvoiceNumber,
            invoice.Total.Amount, invoice.Total.Currency,
            visit.Samples.Select(s => new RegisteredSampleDto(
                s.Id, s.Barcode, s.State.ToString(), s.ConditionName, s.ConditionReadyAtUtc)).ToList());
    }
}
