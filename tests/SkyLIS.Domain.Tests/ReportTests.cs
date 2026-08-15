using FluentAssertions;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Reports;
using Xunit;

namespace SkyLIS.Domain.Tests;

public class LabReportTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 14, 0, 0, TimeSpan.Zero);

    private static LabReport Render(
        ReportKind kind = ReportKind.Final, bool fullyValidated = true,
        bool openCritical = false, int resultCount = 2, int version = 1) =>
        LabReport.Render(Guid.NewGuid(), TenantId, Guid.NewGuid(), Guid.NewGuid(),
            "R-260815-0001", version, kind, "<html>report</html>", "ABC123HASH",
            fullyValidated, openCritical, resultCount, Now);

    [Fact]
    public void Final_render_fires_the_metering_event()
    {
        var report = Render(ReportKind.Final);
        report.Status.Should().Be(ReportStatus.Rendered);
        report.DomainEvents.OfType<ReportRendered>().Should().ContainSingle();
        report.DomainEvents.OfType<ReportFinalized>().Should()
            .ContainSingle("one finalized report per visit is the metering unit (FR-SYS-011)");
    }

    [Fact]
    public void Interim_render_does_not_meter()
    {
        var report = Render(ReportKind.Interim, fullyValidated: false);
        report.DomainEvents.OfType<ReportFinalized>().Should().BeEmpty();
    }

    [Fact]
    public void Final_is_blocked_by_an_open_critical_value()
    {
        var act = () => Render(ReportKind.Final, openCritical: true);
        act.Should().Throw<DomainException>().WithMessage("*critical*cannot reach Final*");
    }

    [Fact]
    public void Final_requires_full_validation_interim_does_not()
    {
        var act = () => Render(ReportKind.Final, fullyValidated: false);
        act.Should().Throw<DomainException>().WithMessage("*INTERIM*");
        Render(ReportKind.Interim, fullyValidated: false).Kind.Should().Be(ReportKind.Interim);
    }

    [Fact]
    public void A_report_requires_at_least_one_result()
    {
        var act = () => Render(resultCount: 0);
        act.Should().Throw<DomainException>().WithMessage("*at least one medically valid result*");
    }

    [Fact]
    public void First_successful_delivery_moves_the_report_to_Delivered()
    {
        var report = Render();
        report.RecordDelivery(Guid.NewGuid(), "email", "mona@example.com", DeliveryOutcome.Failed, Now);
        report.Status.Should().Be(ReportStatus.Rendered, "a failed attempt does not deliver");

        report.RecordDelivery(Guid.NewGuid(), "whatsapp", "+201002345678", DeliveryOutcome.Sent, Now);
        report.Status.Should().Be(ReportStatus.Delivered);
        report.Deliveries.Should().HaveCount(2, "every attempt is logged as evidence");
        report.DomainEvents.OfType<ReportDelivered>().Should().ContainSingle();
    }

    [Fact]
    public void Unknown_delivery_channel_is_rejected()
    {
        var report = Render();
        var act = () => report.RecordDelivery(Guid.NewGuid(), "pigeon", "roof", DeliveryOutcome.Sent, Now);
        act.Should().Throw<DomainException>().WithMessage("*channel*");
    }

    [Fact]
    public void Verification_record_exposes_initials_only()
    {
        var verification = ReportVerification.For(
            Guid.NewGuid(), "NileLab Diagnostics", "Mona El-Sayed", "R-260815-0001", 1, "HASH", Now);
        verification.PatientInitials.Should().Be("M.E.");
        verification.IssuerName.Should().Be("NileLab Diagnostics");
    }
}
