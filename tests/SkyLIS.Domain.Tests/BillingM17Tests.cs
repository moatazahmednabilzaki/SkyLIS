using FluentAssertions;
using SkyLIS.Domain.Billing;
using SkyLIS.Domain.Common;
using Xunit;

namespace SkyLIS.Domain.Tests;

public class BillingM17Tests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    private static Invoice NewInvoice(decimal total = 100m) =>
        Invoice.IssueForVisit(Guid.NewGuid(), TenantId, BranchId, "INV-MAIN-260815-0009", Guid.NewGuid(),
            Money.Of(total, "EGP"), Now);

    [Fact]
    public void Discount_reduces_the_balance_and_requires_a_reason()
    {
        var invoice = NewInvoice(100);
        invoice.ApplyDiscount(20, "Corporate agreement");

        invoice.Balance().Amount.Should().Be(80);
        invoice.NetPayable().Amount.Should().Be(80);

        invoice.CapturePayment(Guid.NewGuid(), Money.Of(80, "EGP"), "cash", Now);
        invoice.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public void Discount_after_payment_is_prohibited()
    {
        var invoice = NewInvoice(100);
        invoice.CapturePayment(Guid.NewGuid(), Money.Of(10, "EGP"), "cash", Now);
        var act = () => invoice.ApplyDiscount(20, "too late");
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Second_discount_is_prohibited()
    {
        var invoice = NewInvoice(100);
        invoice.ApplyDiscount(10, "first");
        var act = () => invoice.ApplyDiscount(5, "second");
        act.Should().Throw<DomainException>().WithMessage("*already applied*");
    }

    [Fact]
    public void Credit_note_waives_the_open_balance_and_marks_adjusted()
    {
        var invoice = NewInvoice(100);
        invoice.CapturePayment(Guid.NewGuid(), Money.Of(30, "EGP"), "cash", Now);

        invoice.ApplyCredit(70);

        invoice.Balance().Amount.Should().Be(0);
        invoice.Status.Should().Be(InvoiceStatus.Adjusted);
    }

    [Fact]
    public void Credit_beyond_the_open_balance_is_prohibited()
    {
        var invoice = NewInvoice(100);
        invoice.CapturePayment(Guid.NewGuid(), Money.Of(60, "EGP"), "cash", Now);
        var act = () => invoice.ApplyCredit(50);
        act.Should().Throw<DomainException>().WithMessage("*within the open balance*");
    }

    [Fact]
    public void Refund_reopens_the_balance_then_credit_closes_it_as_adjusted()
    {
        var invoice = NewInvoice(80);
        invoice.CapturePayment(Guid.NewGuid(), Money.Of(80, "EGP"), "cash", Now);
        invoice.Status.Should().Be(InvoiceStatus.Paid);

        invoice.Refund(Guid.NewGuid(), Money.Of(80, "EGP"), "visit cancelled", Now.AddMinutes(5));
        invoice.Balance().Amount.Should().Be(80, "returned money reopens the receivable");

        invoice.ApplyCredit(80);
        invoice.Status.Should().Be(InvoiceStatus.Adjusted);
        invoice.Balance().Amount.Should().Be(0);
    }

    [Fact]
    public void Refund_beyond_captured_money_is_prohibited()
    {
        var invoice = NewInvoice(100);
        invoice.CapturePayment(Guid.NewGuid(), Money.Of(40, "EGP"), "cash", Now);
        var act = () => invoice.Refund(Guid.NewGuid(), Money.Of(50, "EGP"), "over-refund", Now);
        act.Should().Throw<DomainException>().WithMessage("*exceeds the refundable*");
    }

    [Fact]
    public void Refund_requires_a_reason()
    {
        var invoice = NewInvoice(100);
        invoice.CapturePayment(Guid.NewGuid(), Money.Of(40, "EGP"), "cash", Now);
        var act = () => invoice.Refund(Guid.NewGuid(), Money.Of(10, "EGP"), " ", Now);
        act.Should().Throw<DomainException>().WithMessage("*reason*");
    }
}

public class CashierShiftTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Close_records_declared_vs_expected_and_the_variance()
    {
        var shift = CashierShift.Open(Guid.NewGuid(), TenantId, BranchId, Guid.NewGuid(),
            Money.Of(200, "EGP"), Now);

        shift.Close(declaredCash: 255, expectedCash: 260, Now.AddHours(8));

        shift.Status.Should().Be(ShiftStatus.Closed);
        shift.DeclaredCash.Should().Be(255);
        shift.ExpectedCash.Should().Be(260);
        shift.Variance.Should().Be(-5, "the drawer is 5 short");
    }

    [Fact]
    public void Closing_twice_is_an_invalid_transition()
    {
        var shift = CashierShift.Open(Guid.NewGuid(), TenantId, BranchId, null, Money.Of(0, "EGP"), Now);
        shift.Close(0, 0, Now.AddHours(1));
        var act = () => shift.Close(0, 0, Now.AddHours(2));
        act.Should().Throw<InvalidStateTransitionException>();
    }
}
