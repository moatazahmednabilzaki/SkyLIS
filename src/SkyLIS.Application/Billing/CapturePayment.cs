using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Common;

namespace SkyLIS.Application.Billing;

/// <summary>FR-BIL-001: capture a payment against a visit's invoice (simplified Phase 1 billing).</summary>
public sealed record CapturePaymentCommand(Guid InvoiceId, decimal Amount, string Currency, string Method)
    : ICommand<PaymentResultDto>, IRequirePermission
{
    public string Permission => "billing.payment.capture";
}

public sealed record PaymentResultDto(Guid InvoiceId, string Status, decimal Paid, decimal Balance, string Currency);

internal sealed class CapturePaymentValidator : AbstractValidator<CapturePaymentCommand>
{
    private static readonly string[] Methods = ["cash", "card", "wallet"];

    public CapturePaymentValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.Method)
            .NotEmpty()
            .Must(m => Methods.Contains(m.ToLowerInvariant()))
            .WithMessage("Payment method must be one of: cash, card, wallet.");
    }
}

internal sealed class CapturePaymentHandler : IRequestHandler<CapturePaymentCommand, PaymentResultDto>
{
    private readonly IInvoiceRepository _invoices;
    private readonly IClock _clock;

    public CapturePaymentHandler(IInvoiceRepository invoices, IClock clock)
    {
        _invoices = invoices;
        _clock = clock;
    }

    public async Task<PaymentResultDto> Handle(CapturePaymentCommand request, CancellationToken ct)
    {
        var invoice = await _invoices.GetAsync(request.InvoiceId, ct)
            ?? throw new NotFoundException("Invoice", request.InvoiceId);

        invoice.CapturePayment(
            Guid.CreateVersion7(), Money.Of(request.Amount, request.Currency),
            request.Method.ToLowerInvariant(), _clock.UtcNow);

        return new PaymentResultDto(
            invoice.Id, invoice.Status.ToString(),
            invoice.PaidAmount().Amount, invoice.Balance().Amount, invoice.Total.Currency);
    }
}
