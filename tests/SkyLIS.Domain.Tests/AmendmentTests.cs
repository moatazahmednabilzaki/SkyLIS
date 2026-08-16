using FluentAssertions;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Results;
using Xunit;

namespace SkyLIS.Domain.Tests;

public class AmendmentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Enterer = Guid.NewGuid();
    private static readonly Guid Director = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    private static ResultEvaluation Normal() => new(ResultFlag.Normal, false, false, null, false);

    private static TestResult ValidatedResult()
    {
        var result = TestResult.Enter(
            Guid.NewGuid(), TenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "GLU-F", 92, "mg/dL", Normal(), Enterer, Now);
        result.AcceptTechnical(Guid.NewGuid(), Now.AddMinutes(5));
        result.ValidateMedical(Director, null, "SIGHASH", Now.AddMinutes(10));
        return result;
    }

    [Fact]
    public void Amend_preserves_the_old_value_and_reflags()
    {
        var result = ValidatedResult();

        result.Amend(105, new ResultEvaluation(ResultFlag.High, false, false, 92, false),
            "Transcription error at the bench", Director, "AMEND-SIG", Now.AddHours(1));

        result.IsAmended.Should().BeTrue();
        result.Value.Should().Be(105);
        result.ValueBeforeAmendment.Should().Be(92);
        result.Flag.Should().Be(ResultFlag.High);
        result.Status.Should().Be(ResultStatus.MedicallyValid, "the amended result stays validated");
        result.DomainEvents.OfType<ResultAmended>().Should().ContainSingle(e => e.OldValue == 92 && e.NewValue == 105);
    }

    [Fact]
    public void Amend_before_medical_validation_is_prohibited()
    {
        var result = TestResult.Enter(
            Guid.NewGuid(), TenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "GLU-F", 92, "mg/dL", Normal(), Enterer, Now);

        var act = () => result.Amend(100, Normal(), "reason", Director, "SIG", Now);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void SoD_the_enterer_cannot_amend_their_own_result()
    {
        var result = ValidatedResult();
        var act = () => result.Amend(100, Normal(), "reason", Enterer, "SIG", Now);
        act.Should().Throw<DomainException>().WithMessage("*Segregation of duties*");
    }

    [Fact]
    public void Amendment_to_a_critical_value_opens_a_fresh_critical_cycle()
    {
        var result = ValidatedResult();

        result.Amend(30, new ResultEvaluation(ResultFlag.CriticalLow, true, false, 92, false),
            "Analyzer flag review", Director, "SIG", Now.AddHours(1));

        result.Critical.Should().NotBeNull();
        result.Critical!.State.Should().Be(CriticalState.Flagged);
        result.DomainEvents.OfType<CriticalValueFlagged>().Should().ContainSingle();
    }

    [Fact]
    public void Amendment_requires_a_reason_and_a_different_value()
    {
        var result = ValidatedResult();
        FluentActions.Invoking(() => result.Amend(100, Normal(), " ", Director, "SIG", Now))
            .Should().Throw<DomainException>().WithMessage("*reason*");
        FluentActions.Invoking(() => result.Amend(92, Normal(), "same value", Director, "SIG", Now))
            .Should().Throw<DomainException>().WithMessage("*must differ*");
    }
}
