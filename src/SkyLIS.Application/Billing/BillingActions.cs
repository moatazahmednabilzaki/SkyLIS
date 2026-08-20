using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Billing;
using SkyLIS.Domain.Common;

namespace SkyLIS.Application.Billing;

// ---------- Read side ----------

public sealed record InvoicePaymentDto(
    Guid Id, decimal Amount, string Currency, string Method, bool IsRefund, string? Reason, DateTimeOffset CapturedAtUtc);

public sealed record InvoiceDetailsDto(
    Guid Id, string InvoiceNumber, Guid VisitId, string VisitNumber, string BranchCode, string Status,
    decimal Total, decimal DiscountAmount, string? DiscountReason, decimal CreditedAmount,
    decimal Paid, decimal Refunded, decimal Balance, string Currency,
    IReadOnlyList<InvoicePaymentDto> Payments, IReadOnlyList<CreditNoteDto> CreditNotes);

public sealed record CreditNoteDto(
    Guid Id, string CreditNoteNumber, decimal Amount, string Currency, string Reason, DateTimeOffset IssuedAtUtc);

public sealed record ShiftDto(
    Guid Id, Guid BranchId, string BranchCode, string Status, decimal OpeningFloat, string Currency,
    DateTimeOffset OpenedAtUtc, DateTimeOffset? ClosedAtUtc,
    decimal? DeclaredCash, decimal? ExpectedCash, decimal? Variance);

public sealed record MethodTotalDto(string Method, decimal Captured, decimal Refunded);

public sealed record ZReportDto(
    ShiftDto Shift, IReadOnlyList<MethodTotalDto> ByMethod,
    decimal CashIn, decimal CashOut, decimal ExpectedCash, decimal DeclaredCash, decimal Variance);

public interface IBillingQueries
{
    Task<InvoiceDetailsDto?> GetInvoiceAsync(Guid invoiceId, CancellationToken ct = default);
    /// <summary>The primary invoice for a visit — the billing panel's entry point.</summary>
    Task<InvoiceDetailsDto?> GetInvoiceByVisitAsync(Guid visitId, CancellationToken ct = default);
    /// <summary>Per-method captured/refunded totals for one branch within a time window.</summary>
    Task<IReadOnlyList<MethodTotalDto>> MethodTotalsAsync(
        Guid branchId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
    Task<IReadOnlyList<ShiftDto>> ListShiftsAsync(CancellationToken ct = default);
}

public sealed record GetInvoiceQuery(Guid InvoiceId) : IQuery<InvoiceDetailsDto>, IRequirePermission
{
    public string Permission => "orders.visit.read";
}

internal sealed class GetInvoiceHandler : IRequestHandler<GetInvoiceQuery, InvoiceDetailsDto>
{
    private readonly IBillingQueries _queries;
    public GetInvoiceHandler(IBillingQueries queries) => _queries = queries;

    public async Task<InvoiceDetailsDto> Handle(GetInvoiceQuery request, CancellationToken ct) =>
        await _queries.GetInvoiceAsync(request.InvoiceId, ct)
            ?? throw new NotFoundException("Invoice", request.InvoiceId);
}

public sealed record GetInvoiceByVisitQuery(Guid VisitId) : IQuery<InvoiceDetailsDto>, IRequirePermission
{
    public string Permission => "orders.visit.read";
}

internal sealed class GetInvoiceByVisitHandler : IRequestHandler<GetInvoiceByVisitQuery, InvoiceDetailsDto>
{
    private readonly IBillingQueries _queries;
    public GetInvoiceByVisitHandler(IBillingQueries queries) => _queries = queries;

    public async Task<InvoiceDetailsDto> Handle(GetInvoiceByVisitQuery request, CancellationToken ct) =>
        await _queries.GetInvoiceByVisitAsync(request.VisitId, ct)
            ?? throw new NotFoundException("Invoice for visit", request.VisitId);
}

public sealed record ListShiftsQuery : IQuery<IReadOnlyList<ShiftDto>>, IRequirePermission
{
    public string Permission => "billing.shift.manage";
}

internal sealed class ListShiftsHandler : IRequestHandler<ListShiftsQuery, IReadOnlyList<ShiftDto>>
{
    private readonly IBillingQueries _queries;
    public ListShiftsHandler(IBillingQueries queries) => _queries = queries;

    public Task<IReadOnlyList<ShiftDto>> Handle(ListShiftsQuery request, CancellationToken ct) =>
        _queries.ListShiftsAsync(ct);
}

// ---------- P17.1: discount ----------

public sealed record ApplyDiscountCommand(Guid InvoiceId, decimal Amount, string Reason)
    : ICommand<PaymentResultDto>, IRequirePermission
{
    public string Permission => "billing.invoice.adjust";
}

internal sealed class ApplyDiscountValidator : AbstractValidator<ApplyDiscountCommand>
{
    public ApplyDiscountValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300)
            .WithMessage("A discount reason is mandatory (P17.1).");
    }
}

internal sealed class ApplyDiscountHandler : IRequestHandler<ApplyDiscountCommand, PaymentResultDto>
{
    private readonly IInvoiceRepository _invoices;
    public ApplyDiscountHandler(IInvoiceRepository invoices) => _invoices = invoices;

    public async Task<PaymentResultDto> Handle(ApplyDiscountCommand request, CancellationToken ct)
    {
        var invoice = await _invoices.GetAsync(request.InvoiceId, ct)
            ?? throw new NotFoundException("Invoice", request.InvoiceId);
        invoice.ApplyDiscount(request.Amount, request.Reason);
        return new PaymentResultDto(
            invoice.Id, invoice.Status.ToString(),
            invoice.PaidAmount().Amount, invoice.Balance().Amount, invoice.Total.Currency);
    }
}

// ---------- M17: manual credit note ----------

public sealed record IssueCreditNoteCommand(Guid InvoiceId, decimal Amount, string Reason)
    : ICommand<CreditNoteDto>, IRequirePermission
{
    public string Permission => "billing.invoice.adjust";
}

internal sealed class IssueCreditNoteValidator : AbstractValidator<IssueCreditNoteCommand>
{
    public IssueCreditNoteValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
    }
}

internal sealed class IssueCreditNoteHandler : IRequestHandler<IssueCreditNoteCommand, CreditNoteDto>
{
    private readonly IInvoiceRepository _invoices;
    private readonly ICreditNoteRepository _creditNotes;
    private readonly IBranchRepository _branches;
    private readonly INumberSeriesService _numbers;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public IssueCreditNoteHandler(
        IInvoiceRepository invoices, ICreditNoteRepository creditNotes, IBranchRepository branches,
        INumberSeriesService numbers, ITenantContext tenant, IClock clock)
    {
        _invoices = invoices;
        _creditNotes = creditNotes;
        _branches = branches;
        _numbers = numbers;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<CreditNoteDto> Handle(IssueCreditNoteCommand request, CancellationToken ct)
    {
        var invoice = await _invoices.GetAsync(request.InvoiceId, ct)
            ?? throw new NotFoundException("Invoice", request.InvoiceId);

        var branch = await _branches.GetAsync(invoice.BranchId, ct)
            ?? throw new NotFoundException("Branch", invoice.BranchId);
        var number = await _numbers.NextAsync("creditnote", branch.Code, ct);
        var amount = Money.Of(request.Amount, invoice.Total.Currency);

        invoice.ApplyCredit(amount.Amount);
        var creditNote = CreditNote.Issue(
            Guid.CreateVersion7(), _tenant.TenantId, invoice.BranchId, number,
            invoice.Id, amount, request.Reason, _clock.UtcNow);
        _creditNotes.Add(creditNote);

        return new CreditNoteDto(
            creditNote.Id, creditNote.CreditNoteNumber, creditNote.Amount.Amount,
            creditNote.Amount.Currency, creditNote.Reason, creditNote.IssuedAtUtc);
    }
}

// ---------- M17: refund (SoD: approval permission) ----------

public sealed record RefundPaymentCommand(Guid InvoiceId, decimal Amount, string Reason)
    : ICommand<PaymentResultDto>, IRequirePermission
{
    public string Permission => "billing.refund.approve";
}

internal sealed class RefundPaymentValidator : AbstractValidator<RefundPaymentCommand>
{
    public RefundPaymentValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300)
            .WithMessage("A refund reason is mandatory.");
    }
}

internal sealed class RefundPaymentHandler : IRequestHandler<RefundPaymentCommand, PaymentResultDto>
{
    private readonly IInvoiceRepository _invoices;
    private readonly IClock _clock;

    public RefundPaymentHandler(IInvoiceRepository invoices, IClock clock)
    {
        _invoices = invoices;
        _clock = clock;
    }

    public async Task<PaymentResultDto> Handle(RefundPaymentCommand request, CancellationToken ct)
    {
        var invoice = await _invoices.GetAsync(request.InvoiceId, ct)
            ?? throw new NotFoundException("Invoice", request.InvoiceId);
        invoice.Refund(
            Guid.CreateVersion7(), Money.Of(request.Amount, invoice.Total.Currency),
            request.Reason, _clock.UtcNow);
        return new PaymentResultDto(
            invoice.Id, invoice.Status.ToString(),
            invoice.PaidAmount().Amount, invoice.Balance().Amount, invoice.Total.Currency);
    }
}

// ---------- P17.2: cashier shift & day close ----------

public sealed record OpenShiftCommand(Guid BranchId, decimal OpeningFloat, string Currency)
    : ICommand<Guid>, IRequirePermission
{
    public string Permission => "billing.shift.manage";
}

internal sealed class OpenShiftValidator : AbstractValidator<OpenShiftCommand>
{
    public OpenShiftValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.OpeningFloat).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}

internal sealed class OpenShiftHandler : IRequestHandler<OpenShiftCommand, Guid>
{
    private readonly ICashierShiftRepository _shifts;
    private readonly IBranchRepository _branches;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public OpenShiftHandler(
        ICashierShiftRepository shifts, IBranchRepository branches,
        ITenantContext tenant, ICurrentUser user, IClock clock)
    {
        _shifts = shifts;
        _branches = branches;
        _tenant = tenant;
        _user = user;
        _clock = clock;
    }

    public async Task<Guid> Handle(OpenShiftCommand request, CancellationToken ct)
    {
        var branch = await _branches.GetAsync(request.BranchId, ct)
            ?? throw new NotFoundException("Branch", request.BranchId);
        if (await _shifts.GetOpenByBranchAsync(branch.Id, ct) is not null)
            throw new ConflictException($"Branch {branch.Code} already has an open shift — close it first (P17.2).");

        var shift = CashierShift.Open(
            Guid.CreateVersion7(), _tenant.TenantId, branch.Id, _user.UserId,
            Money.Of(request.OpeningFloat, request.Currency), _clock.UtcNow);
        _shifts.Add(shift);
        return shift.Id;
    }
}

public sealed record CloseShiftCommand(Guid ShiftId, decimal DeclaredCash)
    : ICommand<ZReportDto>, IRequirePermission
{
    public string Permission => "billing.shift.manage";
}

internal sealed class CloseShiftValidator : AbstractValidator<CloseShiftCommand>
{
    public CloseShiftValidator()
    {
        RuleFor(x => x.ShiftId).NotEmpty();
        RuleFor(x => x.DeclaredCash).GreaterThanOrEqualTo(0);
    }
}

internal sealed class CloseShiftHandler : IRequestHandler<CloseShiftCommand, ZReportDto>
{
    private readonly ICashierShiftRepository _shifts;
    private readonly IBillingQueries _queries;
    private readonly IBranchRepository _branches;
    private readonly IClock _clock;

    public CloseShiftHandler(
        ICashierShiftRepository shifts, IBillingQueries queries, IBranchRepository branches, IClock clock)
    {
        _shifts = shifts;
        _queries = queries;
        _branches = branches;
        _clock = clock;
    }

    public async Task<ZReportDto> Handle(CloseShiftCommand request, CancellationToken ct)
    {
        var shift = await _shifts.GetAsync(request.ShiftId, ct)
            ?? throw new NotFoundException("CashierShift", request.ShiftId);

        var now = _clock.UtcNow;
        var byMethod = await _queries.MethodTotalsAsync(shift.BranchId, shift.OpenedAtUtc, now, ct);
        var cashIn = byMethod.Where(m => m.Method == "cash").Sum(m => m.Captured);
        var cashOut = byMethod.Where(m => m.Method == "cash").Sum(m => m.Refunded);
        var expected = shift.OpeningFloat.Amount + cashIn - cashOut;

        shift.Close(request.DeclaredCash, expected, now);

        var branch = await _branches.GetAsync(shift.BranchId, ct);
        return new ZReportDto(
            new ShiftDto(
                shift.Id, shift.BranchId, branch?.Code ?? "?", shift.Status.ToString(),
                shift.OpeningFloat.Amount, shift.OpeningFloat.Currency,
                shift.OpenedAtUtc, shift.ClosedAtUtc, shift.DeclaredCash, shift.ExpectedCash, shift.Variance),
            byMethod, cashIn, cashOut, expected, shift.DeclaredCash!.Value, shift.Variance!.Value);
    }
}
