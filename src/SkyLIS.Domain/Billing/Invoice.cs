using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Billing;

public enum InvoiceStatus { Draft = 0, Issued = 1, PartiallyPaid = 2, Paid = 3, Adjusted = 4 }

/// <summary>
/// Tenant-owned aggregate: Phase 1 billing (SRS Rev 2.0 M17). Invoices are immutable after
/// issue — corrections happen via discounts (pre-payment), credit notes (waive the open
/// balance), and refunds (return captured money). Refund SoD lives in the Application layer.
/// </summary>
public sealed class Invoice : AggregateRoot, ITenantOwned
{
    private readonly List<Payment> _payments = [];

    public Guid TenantId { get; private set; }
    public string InvoiceNumber { get; private set; } = null!;
    /// <summary>The branch the visit was registered at — cash reconciliation runs per branch.</summary>
    public Guid BranchId { get; private set; }
    public Guid VisitId { get; private set; }
    public Money Total { get; private set; } = null!;
    /// <summary>P17.1: discount applied before payment; reason is mandatory and audited.</summary>
    public decimal DiscountAmount { get; private set; }
    public string? DiscountReason { get; private set; }
    /// <summary>Sum of credit notes issued against this invoice (waived receivable).</summary>
    public decimal CreditedAmount { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTimeOffset? IssuedAtUtc { get; private set; }

    public IReadOnlyCollection<Payment> Payments => _payments.AsReadOnly();

    private Invoice() { } // EF

    public static Invoice IssueForVisit(
        Guid id, Guid tenantId, Guid branchId, string invoiceNumber, Guid visitId, Money total, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        if (branchId == Guid.Empty) throw new DomainException("An invoice shall be issued at a branch.");
        if (string.IsNullOrWhiteSpace(invoiceNumber)) throw new DomainException("Invoice number is required.");
        if (total.Amount <= 0) throw new DomainException("Invoice total must be positive.");
        return new Invoice
        {
            Id = id,
            TenantId = tenantId,
            BranchId = branchId,
            InvoiceNumber = invoiceNumber,
            VisitId = visitId,
            Total = total,
            Status = InvoiceStatus.Issued,
            IssuedAtUtc = nowUtc,
        };
    }

    /// <summary>Amount actually owed after discount and credit notes.</summary>
    public Money NetPayable() =>
        Money.Of(Total.Amount - DiscountAmount - CreditedAmount, Total.Currency);

    public Money PaidAmount() => Money.Of(
        _payments.Where(p => !p.IsRefund).Sum(p => p.Amount.Amount), Total.Currency);

    public Money RefundedAmount() => Money.Of(
        _payments.Where(p => p.IsRefund).Sum(p => p.Amount.Amount), Total.Currency);

    public Money Balance() => Money.Of(
        NetPayable().Amount - (PaidAmount().Amount - RefundedAmount().Amount), Total.Currency);

    public void CapturePayment(Guid paymentId, Money amount, string method, DateTimeOffset nowUtc)
    {
        if (Status is not (InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid))
            throw new InvalidStateTransitionException(nameof(Invoice), Status.ToString(), "payment capture");
        EnsureSameCurrency(amount);
        if (amount.Amount <= 0) throw new DomainException("Payment amount must be positive.");
        if (amount.Amount > Balance().Amount)
            throw new DomainException($"Payment {amount} exceeds the open balance {Balance()}.");

        _payments.Add(new Payment(paymentId, TenantId, amount, method, nowUtc));
        RecomputeStatus();
    }

    /// <summary>P17.1: discount before any money is taken — a reduction, never a rewrite.</summary>
    public void ApplyDiscount(decimal amount, string reason)
    {
        if (Status != InvoiceStatus.Issued || _payments.Count > 0)
            throw new InvalidStateTransitionException(nameof(Invoice), Status.ToString(), "discount");
        if (DiscountAmount > 0) throw new DomainException("A discount was already applied to this invoice.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A discount reason is mandatory.");
        if (amount <= 0 || amount >= Total.Amount)
            throw new DomainException("Discount must be positive and below the invoice total.");
        DiscountAmount = decimal.Round(amount, 2);
        DiscountReason = reason.Trim();
    }

    /// <summary>
    /// Registers a credit note against the open balance (called with the CreditNote
    /// aggregate in the same transaction). Waives receivable — never returns money.
    /// </summary>
    public void ApplyCredit(decimal amount)
    {
        if (Status == InvoiceStatus.Draft)
            throw new InvalidStateTransitionException(nameof(Invoice), Status.ToString(), "credit note");
        if (amount <= 0 || amount > Balance().Amount)
            throw new DomainException($"Credit must be positive and within the open balance {Balance()}.");
        CreditedAmount += decimal.Round(amount, 2);
        RecomputeStatus();
    }

    /// <summary>Returns captured money (SoD: requires billing.refund.approve). Reopens the balance.</summary>
    public void Refund(Guid refundId, Money amount, string reason, DateTimeOffset nowUtc)
    {
        EnsureSameCurrency(amount);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A refund reason is mandatory.");
        if (amount.Amount <= 0) throw new DomainException("Refund amount must be positive.");
        var refundable = PaidAmount().Amount - RefundedAmount().Amount;
        if (amount.Amount > refundable)
            throw new DomainException($"Refund {amount} exceeds the refundable amount {refundable} {Total.Currency}.");

        _payments.Add(new Payment(refundId, TenantId, amount, "cash", nowUtc, isRefund: true, reason: reason.Trim()));
        RecomputeStatus();
    }

    private void EnsureSameCurrency(Money amount)
    {
        if (amount.Currency != Total.Currency)
            throw new DomainException($"Cannot combine {amount.Currency} with the invoice currency {Total.Currency}.");
    }

    private void RecomputeStatus()
    {
        var netPaid = PaidAmount().Amount - RefundedAmount().Amount;
        if (Balance().Amount == 0)
            Status = CreditedAmount > 0 || RefundedAmount().Amount > 0 ? InvoiceStatus.Adjusted : InvoiceStatus.Paid;
        else
            Status = netPaid > 0 ? InvoiceStatus.PartiallyPaid : InvoiceStatus.Issued;
    }
}

public sealed class Payment : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public string Method { get; private set; } = null!;
    /// <summary>Refund rows return money; they subtract from cash reconciliation and revenue.</summary>
    public bool IsRefund { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset CapturedAtUtc { get; private set; }

    private Payment() { } // EF

    internal Payment(
        Guid id, Guid tenantId, Money amount, string method, DateTimeOffset capturedAtUtc,
        bool isRefund = false, string? reason = null)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(method)) throw new DomainException("Payment method is required.");
        TenantId = tenantId;
        Amount = amount;
        Method = method.Trim();
        IsRefund = isRefund;
        Reason = reason;
        CapturedAtUtc = capturedAtUtc;
    }
}

/// <summary>
/// Tenant-owned aggregate: a credit note waiving (part of) an invoice's open balance
/// (M17). Auto-issued when a visit is cancelled with an unpaid invoice; manual issues
/// require billing.invoice.adjust. Immutable once created.
/// </summary>
public sealed class CreditNote : AggregateRoot, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string CreditNoteNumber { get; private set; } = null!;
    public Guid InvoiceId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public DateTimeOffset IssuedAtUtc { get; private set; }

    private CreditNote() { } // EF

    public static CreditNote Issue(
        Guid id, Guid tenantId, Guid branchId, string creditNoteNumber,
        Guid invoiceId, Money amount, string reason, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        if (string.IsNullOrWhiteSpace(creditNoteNumber)) throw new DomainException("Credit note number is required.");
        if (amount.Amount <= 0) throw new DomainException("Credit note amount must be positive.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A credit note reason is mandatory.");
        return new CreditNote
        {
            Id = id,
            TenantId = tenantId,
            BranchId = branchId,
            CreditNoteNumber = creditNoteNumber,
            InvoiceId = invoiceId,
            Amount = amount,
            Reason = reason.Trim(),
            IssuedAtUtc = nowUtc,
        };
    }
}

public enum ShiftStatus { Open = 0, Closed = 1 }

/// <summary>
/// Tenant-owned aggregate: a cashier shift at a branch (P17.2). One open shift per branch;
/// closing reconciles declared cash against expected cash (opening float + cash in − cash
/// out during the shift) and records the variance — the Z-report.
/// </summary>
public sealed class CashierShift : AggregateRoot, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid? OpenedByUserId { get; private set; }
    public ShiftStatus Status { get; private set; }
    public Money OpeningFloat { get; private set; } = null!;
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public decimal? DeclaredCash { get; private set; }
    public decimal? ExpectedCash { get; private set; }
    public decimal? Variance { get; private set; }

    private CashierShift() { } // EF

    public static CashierShift Open(
        Guid id, Guid tenantId, Guid branchId, Guid? openedByUserId, Money openingFloat, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        if (branchId == Guid.Empty) throw new DomainException("A shift opens at a branch.");
        return new CashierShift
        {
            Id = id,
            TenantId = tenantId,
            BranchId = branchId,
            OpenedByUserId = openedByUserId,
            Status = ShiftStatus.Open,
            OpeningFloat = openingFloat,
            OpenedAtUtc = nowUtc,
        };
    }

    /// <summary>Close with the counted drawer; expected cash is computed by the caller from payments.</summary>
    public void Close(decimal declaredCash, decimal expectedCash, DateTimeOffset nowUtc)
    {
        if (Status != ShiftStatus.Open)
            throw new InvalidStateTransitionException(nameof(CashierShift), Status.ToString(), ShiftStatus.Closed.ToString());
        if (declaredCash < 0) throw new DomainException("Declared cash cannot be negative.");
        Status = ShiftStatus.Closed;
        ClosedAtUtc = nowUtc;
        DeclaredCash = decimal.Round(declaredCash, 2);
        ExpectedCash = decimal.Round(expectedCash, 2);
        Variance = DeclaredCash - ExpectedCash;
    }
}
