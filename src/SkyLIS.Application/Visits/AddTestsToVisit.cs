using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Billing;
using SkyLIS.Domain.Visits;

namespace SkyLIS.Application.Visits;

public sealed record AddedTestsDto(
    Guid VisitId, Guid SupplementaryInvoiceId, string SupplementaryInvoiceNumber,
    decimal AddedAmount, string Currency, IReadOnlyList<RegisteredSampleDto> NewSamples);

/// <summary>
/// P05.4: add tests to an OPEN visit. New tests land on NEW samples (specimen integrity);
/// billing issues a supplementary invoice for the added amount in the same transaction.
/// </summary>
public sealed record AddTestsToVisitCommand(Guid VisitId, IReadOnlyList<Guid> TestIds)
    : ICommand<AddedTestsDto>, IRequirePermission
{
    public string Permission => "orders.visit.create";
}

internal sealed class AddTestsToVisitValidator : AbstractValidator<AddTestsToVisitCommand>
{
    public AddTestsToVisitValidator()
    {
        RuleFor(x => x.VisitId).NotEmpty();
        RuleFor(x => x.TestIds).NotEmpty().WithMessage("Add at least one test.");
    }
}

internal sealed class AddTestsToVisitHandler : IRequestHandler<AddTestsToVisitCommand, AddedTestsDto>
{
    private readonly IVisitRepository _visits;
    private readonly ILabTestRepository _tests;
    private readonly ISampleTypeRepository _sampleTypes;
    private readonly IBranchRepository _branches;
    private readonly IInvoiceRepository _invoices;
    private readonly INumberSeriesService _numbers;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public AddTestsToVisitHandler(
        IVisitRepository visits, ILabTestRepository tests, ISampleTypeRepository sampleTypes,
        IBranchRepository branches, IInvoiceRepository invoices, INumberSeriesService numbers,
        ITenantContext tenant, IClock clock)
    {
        _visits = visits;
        _tests = tests;
        _sampleTypes = sampleTypes;
        _branches = branches;
        _invoices = invoices;
        _numbers = numbers;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<AddedTestsDto> Handle(AddTestsToVisitCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetAsync(request.VisitId, ct)
            ?? throw new NotFoundException("Visit", request.VisitId);

        var testIds = request.TestIds.Distinct().ToArray();
        var tests = await _tests.GetManyAsync(testIds, ct);
        var missing = testIds.Except(tests.Select(t => t.Id)).ToList();
        if (missing.Count > 0)
            throw new NotFoundException("LabTest", string.Join(", ", missing));

        var conditionIds = tests.Where(t => t.RequiredConditionId is not null)
                                .Select(t => t.RequiredConditionId!.Value).Distinct().ToArray();
        var conditions = await _sampleTypes.GetConditionsAsync(conditionIds, ct);
        var conditionById = conditions.ToDictionary(c => c.Id);

        var inputs = tests.Select(t => new SpecimenPlanner.PlanInput(
            t, t.RequiredConditionId is null ? null : conditionById[t.RequiredConditionId.Value])).ToList();

        // Barcodes continue the visit's sample sequence (V-…-S3, S4, …).
        var barcodeIndex = visit.Samples.Count;
        var plan = SpecimenPlanner.Compute(
            inputs,
            Guid.CreateVersion7,
            _ => $"{visit.VisitNumber}-S{++barcodeIndex}");

        var now = _clock.UtcNow;
        visit.AddTests(plan.Tests, plan.Samples, now);

        var branch = await _branches.GetAsync(visit.BranchId, ct)
            ?? throw new NotFoundException("Branch", visit.BranchId);
        var currency = tests[0].Price!.Currency;
        var addedAmount = plan.Tests.Sum(t => t.Price!.Amount);
        var invoiceNumber = await _numbers.NextAsync("invoice", branch.Code, ct);
        var invoice = Invoice.IssueForVisit(
            Guid.CreateVersion7(), _tenant.TenantId, branch.Id, invoiceNumber, visit.Id,
            Domain.Common.Money.Of(addedAmount, currency), now);
        _invoices.Add(invoice);

        return new AddedTestsDto(
            visit.Id, invoice.Id, invoice.InvoiceNumber, addedAmount, currency,
            plan.Samples.Select(s =>
            {
                var created = visit.Samples.First(v => v.Id == s.SampleId);
                return new RegisteredSampleDto(
                    created.Id, created.Barcode, created.State.ToString(),
                    created.ConditionName, created.ConditionReadyAtUtc);
            }).ToList());
    }
}
