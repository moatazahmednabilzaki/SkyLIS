using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Billing;

public enum InvoiceStatus { Draft = 0, Issued = 1, PartiallyPaid = 2, Paid = 3, Adjusted = 4 }

/// <summary>
/// Tenant-owned aggregate: simplified Phase 1 billing (SRS Rev 2.0 M17).
/// Invoices are immutable after issue — corrections happen via credit notes (later slice;
/// the Adjusted terminal state is reserved for them). Refund SoD lives in the Application layer.
/// </summary>
public sealed class Invoice : AggregateRoot, ITenantOwned
{
    private readonly List<Payment> _payments = [];

    public Guid TenantId { get; private set; }
    public string InvoiceNumber { get; private set; } = null!;
    public Guid VisitId { get; private set; }
    public Money Total { get; private set; } = null!;
    public InvoiceStatus Status { get; private set; }
    public DateTimeOffset? IssuedAtUtc { get; private set; }

    public IReadOnlyCollection<Payment> Payments => _payments.AsReadOnly();

    private Invoice() { } // EF

    public static Invoice IssueForVisit(Guid id, Guid tenantId, string invoiceNumber, Guid visitId, Money total, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        if (string.IsNullOrWhiteSpace(invoiceNumber)) throw new DomainException("Invoice number is required.");
        if (total.Amount <= 0) throw new DomainException("Invoice total must be positive.");
        return new Invoice
        {
            Id = id,
            TenantId = tenantId,
            InvoiceNumber = invoiceNumber,
            VisitId = visitId,
            Total = total,
            Status = InvoiceStatus.Issued,
            IssuedAtUtc = nowUtc,
        };
    }

    public Money PaidAmount() =>
        _payments.Aggregate(Money.Zero(Total.Currency), (acc, p) => acc.Add(p.Amount));

    public Money Balance() => Total.Subtract(PaidAmount());

    public void CapturePayment(Guid paymentId, Money amount, string method, DateTimeOffset nowUtc)
    {
        if (Status is not (InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid))
            throw new InvalidStateTransitionException(nameof(Invoice), Status.ToString(), "payment capture");
        if (amount.Amount <= 0) throw new DomainException("Payment amount must be positive.");
        if (amount.Amount > Balance().Amount)
            throw new DomainException($"Payment {amount} exceeds the open balance {Balance()}.");

        _payments.Add(new Payment(paymentId, TenantId, amount, method, nowUtc));
        Status = Balance().Amount == 0 ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
    }
}

public sealed class Payment : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public string Method { get; private set; } = null!;
    public DateTimeOffset CapturedAtUtc { get; private set; }

    private Payment() { } // EF

    internal Payment(Guid id, Guid tenantId, Money amount, string method, DateTimeOffset capturedAtUtc)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(method)) throw new DomainException("Payment method is required.");
        TenantId = tenantId;
        Amount = amount;
        Method = method.Trim();
        CapturedAtUtc = capturedAtUtc;
    }
}
