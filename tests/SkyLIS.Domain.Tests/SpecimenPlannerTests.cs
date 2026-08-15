using FluentAssertions;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Visits;
using Xunit;

namespace SkyLIS.Domain.Tests;

public class SpecimenPlannerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static SampleType VenousBlood(out SampleCondition fasting, out SampleCondition pp2h, out SampleCondition random)
    {
        var type = SampleType.Create(Guid.NewGuid(), TenantId, "Venous blood", "EDTA");
        random = type.AddCondition(Guid.NewGuid(), "Random", null, "VB-G1");
        fasting = type.AddCondition(Guid.NewGuid(), "Fasting 8h", null, "VB-G1");
        pp2h = type.AddCondition(Guid.NewGuid(), "Post-prandial +2h", 120, "VB-G2");
        return type;
    }

    private static LabTest ActiveTest(string code, Guid sampleTypeId, Guid? conditionId) =>
        LabTest.CreateFromPlatformSeed(Guid.NewGuid(), TenantId, code, code + " test", "Chemistry",
            sampleTypeId, conditionId, Money.Of(100, "EGP"));

    [Fact]
    public void Compatible_conditions_consolidate_onto_one_sample_id()
    {
        var type = VenousBlood(out var fasting, out _, out var random);
        var t1 = ActiveTest("GLU-F", type.Id, fasting.Id);
        var t2 = ActiveTest("CBC", type.Id, random.Id);

        var plan = SpecimenPlanner.Compute(
            [new(t1, fasting), new(t2, random)],
            Guid.NewGuid, i => $"S{i}");

        plan.Samples.Should().HaveCount(1, "Random and Fasting share compatibility group VB-G1");
        plan.Tests.Should().HaveCount(2);
        plan.Tests.Select(t => t.SampleId).Distinct().Should().HaveCount(1);
    }

    [Fact]
    public void Delayed_condition_forces_a_separate_reserved_sample()
    {
        var type = VenousBlood(out var fasting, out var pp2h, out _);
        var t1 = ActiveTest("GLU-F", type.Id, fasting.Id);
        var t2 = ActiveTest("GLU-PP", type.Id, pp2h.Id);

        var plan = SpecimenPlanner.Compute(
            [new(t1, fasting), new(t2, pp2h)],
            Guid.NewGuid, i => $"S{i}");

        plan.Samples.Should().HaveCount(2, "PP +2h is in a different compatibility group");
        plan.Samples.Should().ContainSingle(s => s.DelayMinutes == 120 && s.ConditionName == "Post-prandial +2h");
    }

    [Fact]
    public void Duplicate_test_selection_is_flagged()
    {
        var type = VenousBlood(out var fasting, out _, out _);
        var t1 = ActiveTest("GLU-F", type.Id, fasting.Id);

        var act = () => SpecimenPlanner.Compute(
            [new(t1, fasting), new(t1, fasting)],
            Guid.NewGuid, i => $"S{i}");

        act.Should().Throw<DomainException>().WithMessage("*Duplicate*GLU-F*");
    }

    [Fact]
    public void Inactive_tests_cannot_be_planned()
    {
        var type = VenousBlood(out _, out _, out _);
        var pending = LabTest.CreateFromPlatformPush(Guid.NewGuid(), TenantId, "D-DIM", "D-Dimer",
            "Coagulation", type.Id, null); // PendingActivation — no price yet

        var act = () => SpecimenPlanner.Compute([new(pending, null)], Guid.NewGuid, i => $"S{i}");
        act.Should().Throw<DomainException>().WithMessage("*not active*");
    }
}
