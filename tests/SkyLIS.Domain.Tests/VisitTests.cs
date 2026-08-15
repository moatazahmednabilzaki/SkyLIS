using FluentAssertions;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Visits;
using Xunit;

namespace SkyLIS.Domain.Tests;

public class VisitTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    private static (Visit Visit, Guid ReadySampleId, Guid ReservedSampleId) RegisterVisitWithReservation()
    {
        var readySample = Guid.NewGuid();
        var reservedSample = Guid.NewGuid();
        var samples = new List<PlannedSample>
        {
            new(readySample, "V-1-S1", Guid.NewGuid(), null, null),
            new(reservedSample, "V-1-S2", Guid.NewGuid(), "PP +2h", 120),
        };
        var tests = new List<PlannedTest>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "GLU-F", readySample, Money.Of(80, "EGP")),
            new(Guid.NewGuid(), Guid.NewGuid(), "GLU-PP", reservedSample, Money.Of(80, "EGP")),
        };
        var visit = Visit.Register(Guid.NewGuid(), TenantId, BranchId, "V-MAIN-260815-0001", Guid.NewGuid(),
            tests, samples, isStat: false, statReason: null, Now);
        return (visit, readySample, reservedSample);
    }

    [Fact]
    public void Register_creates_reserved_sample_with_condition_window_and_raises_events()
    {
        var (visit, _, reservedId) = RegisterVisitWithReservation();

        visit.Status.Should().Be(VisitStatus.Registered);
        var reserved = visit.Samples.Single(s => s.Id == reservedId);
        reserved.State.Should().Be(SampleState.ConditionPending);
        reserved.ConditionReadyAtUtc.Should().Be(Now.AddMinutes(120));
        visit.DomainEvents.OfType<VisitRegistered>().Should().ContainSingle();
        visit.DomainEvents.OfType<SampleReserved>().Should().ContainSingle();
    }

    [Fact]
    public void Register_rejects_zero_tests()
    {
        var act = () => Visit.Register(Guid.NewGuid(), TenantId, BranchId, "V-1", Guid.NewGuid(),
            [], [new PlannedSample(Guid.NewGuid(), "B", Guid.NewGuid(), null, null)], false, null, Now);
        act.Should().Throw<DomainException>().WithMessage("*zero tests*");
    }

    [Fact]
    public void Register_rejects_unresolved_price()
    {
        var sampleId = Guid.NewGuid();
        var act = () => Visit.Register(Guid.NewGuid(), TenantId, BranchId, "V-1", Guid.NewGuid(),
            [new PlannedTest(Guid.NewGuid(), Guid.NewGuid(), "X", sampleId, null)],
            [new PlannedSample(sampleId, "B", Guid.NewGuid(), null, null)], false, null, Now);
        act.Should().Throw<DomainException>().WithMessage("*unresolved price*");
    }

    [Fact]
    public void Stat_requires_reason()
    {
        var sampleId = Guid.NewGuid();
        var act = () => Visit.Register(Guid.NewGuid(), TenantId, BranchId, "V-1", Guid.NewGuid(),
            [new PlannedTest(Guid.NewGuid(), Guid.NewGuid(), "X", sampleId, Money.Of(10, "EGP"))],
            [new PlannedSample(sampleId, "B", Guid.NewGuid(), null, null)], isStat: true, statReason: " ", Now);
        act.Should().Throw<DomainException>().WithMessage("*STAT*");
    }

    [Fact]
    public void Reserved_sample_cannot_be_collected_before_condition_window()
    {
        var (visit, _, reservedId) = RegisterVisitWithReservation();
        var act = () => visit.CollectSample(reservedId, Now.AddMinutes(30));
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reserved_sample_collects_after_condition_window_opens()
    {
        var (visit, _, reservedId) = RegisterVisitWithReservation();
        visit.CollectSample(reservedId, Now.AddMinutes(121));
        visit.Samples.Single(s => s.Id == reservedId).State.Should().Be(SampleState.Collected);
    }

    [Fact]
    public void Collect_then_receive_walks_the_sample_state_machine()
    {
        var (visit, readyId, reservedId) = RegisterVisitWithReservation();

        visit.CollectSample(readyId, Now.AddMinutes(5));
        visit.Status.Should().Be(VisitStatus.Registered, "the reserved sample is still pending");

        visit.CollectSample(reservedId, Now.AddMinutes(130));
        visit.Status.Should().Be(VisitStatus.Collected);

        visit.ReceiveSample(readyId, Now.AddMinutes(140));
        visit.Status.Should().Be(VisitStatus.Received);
        visit.Samples.Single(s => s.Id == readyId).State.Should().Be(SampleState.Received);
    }

    [Fact]
    public void Receiving_an_uncollected_sample_is_an_invalid_transition()
    {
        var (visit, readyId, _) = RegisterVisitWithReservation();
        var act = () => visit.ReceiveSample(readyId, Now);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Rejection_spawns_recollection_and_rebinds_tests()
    {
        var (visit, readyId, _) = RegisterVisitWithReservation();
        visit.CollectSample(readyId, Now.AddMinutes(5));

        var recollectionId = Guid.NewGuid();
        var recollection = visit.RejectSample(readyId, "HEMOLYZED", recollectionId, "V-1-S1R", Now.AddMinutes(20));

        visit.Samples.Single(s => s.Id == readyId).State.Should().Be(SampleState.Rejected);
        recollection.State.Should().Be(SampleState.ReadyToCollect);
        visit.Tests.Where(t => t.TestCode == "GLU-F").Should()
            .OnlyContain(t => t.SampleId == recollectionId && t.Status == VisitTestStatus.AwaitingSample);
        visit.DomainEvents.OfType<SampleRejected>().Should().ContainSingle();
    }

    [Fact]
    public void Rejecting_a_sample_not_yet_collected_is_prohibited()
    {
        var (visit, readyId, _) = RegisterVisitWithReservation();
        var act = () => visit.RejectSample(readyId, "HEMOLYZED", Guid.NewGuid(), "R", Now);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Cancel_is_blocked_after_reported()
    {
        var (visit, _, _) = RegisterVisitWithReservation();
        visit.Cancel("patient request");
        visit.Status.Should().Be(VisitStatus.Cancelled);
        visit.Tests.Should().OnlyContain(t => t.Status == VisitTestStatus.Cancelled);
    }
}
