using FluentAssertions;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Results;
using Xunit;

namespace SkyLIS.Domain.Tests;

public class ResultEvaluatorTests
{
    private static LabTest PotassiumTest(bool autoVerify = false)
    {
        var test = LabTest.CreateFromPlatformSeed(Guid.NewGuid(), Guid.NewGuid(), "K", "Potassium",
            "Chemistry", Guid.NewGuid(), null, Money.Of(90, "EGP"));
        test.SetResultSchema(ResultSchema.Of(
            unit: "mmol/L", refLow: 3.5m, refHigh: 5.1m, criticalLow: 2.5m, criticalHigh: 6.0m,
            absurdLow: 1.0m, absurdHigh: 12.0m, autoVerify: autoVerify, deltaThresholdPercent: 50m));
        return test;
    }

    [Theory]
    [InlineData(4.2, ResultFlag.Normal)]
    [InlineData(3.2, ResultFlag.Low)]
    [InlineData(5.5, ResultFlag.High)]
    [InlineData(2.1, ResultFlag.CriticalLow)]
    [InlineData(6.9, ResultFlag.CriticalHigh)]
    public void Flags_follow_reference_and_critical_limits(decimal value, ResultFlag expected)
    {
        var evaluation = ResultEvaluator.Evaluate(PotassiumTest(), value, previousValue: null);
        evaluation.Flag.Should().Be(expected);
        evaluation.IsCritical.Should().Be(expected is ResultFlag.CriticalLow or ResultFlag.CriticalHigh);
    }

    [Fact]
    public void Absurd_values_cannot_be_saved()
    {
        var act = () => ResultEvaluator.Evaluate(PotassiumTest(), 25m, null);
        act.Should().Throw<DomainException>().WithMessage("*absurd*");
    }

    [Fact]
    public void Delta_check_flags_implausible_change()
    {
        var evaluation = ResultEvaluator.Evaluate(PotassiumTest(), 4.5m, previousValue: 2.8m);
        evaluation.DeltaFlagged.Should().BeTrue("60% change exceeds the 50% threshold");
        ResultEvaluator.Evaluate(PotassiumTest(), 4.5m, previousValue: 4.0m).DeltaFlagged.Should().BeFalse();
    }

    [Fact]
    public void Missing_schema_blocks_entry()
    {
        var test = LabTest.CreateFromPlatformSeed(Guid.NewGuid(), Guid.NewGuid(), "X", "X", "Chemistry",
            Guid.NewGuid(), null, Money.Of(10, "EGP"));
        var act = () => ResultEvaluator.Evaluate(test, 1m, null);
        act.Should().Throw<DomainException>().WithMessage("*no result schema*");
    }
}

public class TestResultTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Enterer = Guid.NewGuid();
    private static readonly Guid Supervisor = Guid.NewGuid();
    private static readonly Guid Director = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static TestResult Enter(ResultEvaluation evaluation) =>
        TestResult.Enter(Guid.NewGuid(), TenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "K", 6.9m, "mmol/L", evaluation, Enterer, Now);

    private static ResultEvaluation Normal(bool autoVerify = false) =>
        new(ResultFlag.Normal, IsCritical: false, DeltaFlagged: false, PreviousValue: null, AutoVerify: autoVerify);

    private static ResultEvaluation CriticalHigh() =>
        new(ResultFlag.CriticalHigh, IsCritical: true, DeltaFlagged: false, PreviousValue: null, AutoVerify: true);

    [Fact]
    public void Clean_result_auto_verifies_when_enabled()
    {
        var result = Enter(Normal(autoVerify: true));
        result.Status.Should().Be(ResultStatus.TechnicallyValid);
        result.TechnicallyValidatedBy.Should().BeNull("auto-verification is by the system");
        result.DomainEvents.OfType<ResultTechnicallyValid>().Should().ContainSingle(e => e.AutoVerified);
    }

    [Fact]
    public void Critical_result_never_auto_verifies_and_opens_notification()
    {
        var result = Enter(CriticalHigh());
        result.Status.Should().Be(ResultStatus.Entered, "critical results always need human review");
        result.Critical.Should().NotBeNull();
        result.Critical!.State.Should().Be(CriticalState.Flagged);
        result.DomainEvents.OfType<CriticalValueFlagged>().Should().ContainSingle();
    }

    [Fact]
    public void Two_tier_validation_with_sod()
    {
        var result = Enter(Normal());
        result.AcceptTechnical(Supervisor, Now);
        result.Status.Should().Be(ResultStatus.TechnicallyValid);

        // SoD: the enterer cannot medically validate their own result.
        var sodViolation = () => result.ValidateMedical(Enterer, null, "HASH", Now);
        sodViolation.Should().Throw<DomainException>().WithMessage("*Segregation of duties*");

        result.ValidateMedical(Director, "Consistent with dehydration.", "HASH", Now);
        result.Status.Should().Be(ResultStatus.MedicallyValid);
        result.SignatureHash.Should().Be("HASH");
        result.DomainEvents.OfType<ResultMedicallyValid>().Should().ContainSingle();
    }

    [Fact]
    public void Medical_validation_requires_technical_first()
    {
        var result = Enter(Normal());
        var act = () => result.ValidateMedical(Director, null, "HASH", Now);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Rerun_voids_the_result_and_requires_reason()
    {
        var result = Enter(Normal());
        FluentActions.Invoking(() => result.OrderRerun(" ")).Should().Throw<DomainException>();
        result.OrderRerun("hemolysis suspected");
        result.Status.Should().Be(ResultStatus.RerunOrdered);

        var act = () => result.AcceptTechnical(Supervisor, Now);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Critical_closure_requires_read_back()
    {
        var result = Enter(CriticalHigh());
        result.DocumentCriticalCall("Dr. Hossam Fathy", "+201224567890", readBackConfirmed: false, Now);
        result.Critical!.State.Should().Be(CriticalState.ReadBackDocumented, "no read-back -> stays open");

        result.DocumentCriticalCall("Dr. Hossam Fathy", "+201224567890", readBackConfirmed: true, Now);
        result.Critical.State.Should().Be(CriticalState.Closed);
        result.DomainEvents.OfType<CriticalValueClosed>().Should().ContainSingle();

        var act = () => result.DocumentCriticalCall("X", "1234", true, Now);
        act.Should().Throw<DomainException>().WithMessage("*already closed*");
    }

    [Fact]
    public void Non_critical_result_has_nothing_to_document()
    {
        var result = Enter(Normal());
        var act = () => result.DocumentCriticalCall("X", "+20100000000", true, Now);
        act.Should().Throw<DomainException>().WithMessage("*no critical value*");
    }
}
