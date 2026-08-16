using FluentValidation;
using MediatR;
using SkyLIS.Application.Billing;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Billing;

namespace SkyLIS.Application.Visits;

public sealed record CancelledVisitDto(
    Guid VisitId, string VisitStatus, string InvoiceStatus,
    CreditNoteDto? AutoCreditNote);

/// <summary>
/// M05/M17: cancel a visit with a mandatory reason. The unpaid balance is waived by an
/// automatically issued credit note in the SAME transaction (SRS Rev 2.0 P17.1); money
/// already captured stays until an explicit refund (billing.refund.approve, SoD).
/// </summary>
public sealed record CancelVisitCommand(Guid VisitId, string Reason) : ICommand<CancelledVisitDto>, IRequirePermission
{
    public string Permission => "orders.visit.cancel";
}

internal sealed class CancelVisitValidator : AbstractValidator<CancelVisitCommand>
{
    public CancelVisitValidator()
    {
        RuleFor(x => x.VisitId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300)
            .WithMessage("A cancellation reason is mandatory.");
    }
}

internal sealed class CancelVisitHandler : IRequestHandler<CancelVisitCommand, CancelledVisitDto>
{
    private readonly IVisitRepository _visits;
    private readonly IInvoiceRepository _invoices;
    private readonly ICreditNoteRepository _creditNotes;
    private readonly IBranchRepository _branches;
    private readonly INumberSeriesService _numbers;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public CancelVisitHandler(
        IVisitRepository visits, IInvoiceRepository invoices, ICreditNoteRepository creditNotes,
        IBranchRepository branches, INumberSeriesService numbers, ITenantContext tenant, IClock clock)
    {
        _visits = visits;
        _invoices = invoices;
        _creditNotes = creditNotes;
        _branches = branches;
        _numbers = numbers;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<CancelledVisitDto> Handle(CancelVisitCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetAsync(request.VisitId, ct)
            ?? throw new NotFoundException("Visit", request.VisitId);
        var invoices = await _invoices.GetAllByVisitAsync(visit.Id, ct);
        if (invoices.Count == 0)
            throw new NotFoundException("Invoice for visit", request.VisitId);

        visit.Cancel(request.Reason);

        // Every invoice of the visit (original + supplementary add-ons) gets its open
        // balance waived; the first credit note is surfaced in the response.
        CreditNoteDto? autoCreditNote = null;
        foreach (var invoice in invoices)
        {
            var openBalance = invoice.Balance().Amount;
            if (openBalance <= 0) continue;

            var branch = await _branches.GetAsync(invoice.BranchId, ct)
                ?? throw new NotFoundException("Branch", invoice.BranchId);
            var number = await _numbers.NextAsync("creditnote", branch.Code, ct);

            invoice.ApplyCredit(openBalance);
            var creditNote = CreditNote.Issue(
                Guid.CreateVersion7(), _tenant.TenantId, invoice.BranchId, number, invoice.Id,
                Domain.Common.Money.Of(openBalance, invoice.Total.Currency),
                $"Visit cancelled: {request.Reason.Trim()}", _clock.UtcNow);
            _creditNotes.Add(creditNote);

            autoCreditNote ??= new CreditNoteDto(
                creditNote.Id, creditNote.CreditNoteNumber, creditNote.Amount.Amount,
                creditNote.Amount.Currency, creditNote.Reason, creditNote.IssuedAtUtc);
        }

        return new CancelledVisitDto(
            visit.Id, visit.Status.ToString(), invoices[0].Status.ToString(), autoCreditNote);
    }
}
