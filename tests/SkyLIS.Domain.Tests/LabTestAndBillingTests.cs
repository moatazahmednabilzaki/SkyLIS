using FluentAssertions;
using SkyLIS.Domain.Billing;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Common;
using Xunit;

namespace SkyLIS.Domain.Tests;

public class LabTestTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Pushed_test_arrives_pending_activation_and_needs_a_price()
    {
        var test = LabTest.CreateFromPlatformPush(Guid.NewGuid(), TenantId, "D-DIM", "D-Dimer",
            "Coagulation", Guid.NewGuid(), null);

        test.Status.Should().Be(TestStatus.PendingActivation);
        test.Origin.Should().Be(TestOrigin.PlatformPush);

        var act = test.Activate; // no price set
        act.Should().Throw<DomainException>().WithMessage("*price*");

        test.ActivatePushedTest(Money.Of(450, "EGP"));
        test.Status.Should().Be(TestStatus.Active);
        test.Price!.Amount.Should().Be(450);
    }

    [Fact]
    public void Tenant_test_walks_draft_review_approve_active()
    {
        var test = LabTest.CreateTenantTest(Guid.NewGuid(), TenantId, "CA-153", "CA 15-3",
            "Immunoassay", Guid.NewGuid(), null, Money.Of(650, "EGP"));

        test.Origin.Should().Be(TestOrigin.TenantDefined);
        test.Status.Should().Be(TestStatus.Draft);
        test.SubmitForReview();
        test.Approve();
        test.Status.Should().Be(TestStatus.Active);
    }

    [Fact]
    public void Only_active_tests_can_retire()
    {
        var test = LabTest.CreateTenantTest(Guid.NewGuid(), TenantId, "X1", "X", "Chemistry",
            Guid.NewGuid(), null, Money.Of(10, "EGP"));
        var act = test.Retire;
        act.Should().Throw<InvalidStateTransitionException>();
    }
}

public class InvoiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    private static Invoice NewInvoice(decimal total = 604.20m) =>
        Invoice.IssueForVisit(Guid.NewGuid(), TenantId, "INV-260815-0001", Guid.NewGuid(),
            Money.Of(total, "EGP"), Now);

    [Fact]
    public void Partial_then_full_payment_walks_the_state_machine()
    {
        var invoice = NewInvoice();
        invoice.Status.Should().Be(InvoiceStatus.Issued);

        invoice.CapturePayment(Guid.NewGuid(), Money.Of(300, "EGP"), "cash", Now);
        invoice.Status.Should().Be(InvoiceStatus.PartiallyPaid);
        invoice.Balance().Amount.Should().Be(304.20m);

        invoice.CapturePayment(Guid.NewGuid(), Money.Of(304.20m, "EGP"), "card", Now);
        invoice.Status.Should().Be(InvoiceStatus.Paid);
        invoice.Balance().Amount.Should().Be(0);
    }

    [Fact]
    public void Overpayment_is_rejected()
    {
        var invoice = NewInvoice(100);
        var act = () => invoice.CapturePayment(Guid.NewGuid(), Money.Of(150, "EGP"), "cash", Now);
        act.Should().Throw<DomainException>().WithMessage("*exceeds*");
    }

    [Fact]
    public void Currency_mismatch_is_rejected()
    {
        var invoice = NewInvoice(100);
        var act = () => invoice.CapturePayment(Guid.NewGuid(), Money.Of(50, "USD"), "cash", Now);
        act.Should().Throw<DomainException>().WithMessage("*Cannot combine*");
    }

    [Fact]
    public void Money_rejects_negative_and_bad_currency()
    {
        FluentActions.Invoking(() => Money.Of(-1, "EGP")).Should().Throw<DomainException>();
        FluentActions.Invoking(() => Money.Of(10, "POUNDS")).Should().Throw<DomainException>();
    }
}
