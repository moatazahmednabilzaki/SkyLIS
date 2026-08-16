using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Visits;

/// <summary>
/// Domain service: computes the minimal specimen plan for a set of tests using the
/// sample-condition compatibility groups (SRS Rev 2.0 P03.4 / P05.2).
/// Tests whose (sample type, compatibility group) match consolidate onto ONE Sample ID;
/// a delayed condition (e.g., post-prandial +2h) yields a reserved sample.
/// </summary>
public static class SpecimenPlanner
{
    /// <summary>PriceOverride carries panel-allocated prices (P03.5); null = the test's own price.</summary>
    public sealed record PlanInput(LabTest Test, SampleCondition? Condition, Money? PriceOverride = null);
    public sealed record Plan(IReadOnlyList<PlannedSample> Samples, IReadOnlyList<PlannedTest> Tests);

    public static Plan Compute(
        IReadOnlyList<PlanInput> inputs,
        Func<Guid> newId,
        Func<int, string> barcodeForIndex)
    {
        if (inputs.Count == 0) throw new DomainException("Specimen planning requires at least one test.");

        var duplicates = inputs.GroupBy(i => i.Test.Code).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new DomainException($"Duplicate test selection: {string.Join(", ", duplicates)}.");

        var inactive = inputs.Where(i => i.Test.Status != TestStatus.Active).Select(i => i.Test.Code).ToList();
        if (inactive.Count > 0)
            throw new DomainException($"Tests not active in the catalogue: {string.Join(", ", inactive)}.");

        var samples = new List<PlannedSample>();
        var tests = new List<PlannedTest>();

        var groups = inputs.GroupBy(i => (
            i.Test.SampleTypeId,
            Group: i.Condition?.CompatibilityGroup ?? "DEFAULT",
            i.Condition?.DelayMinutes));

        var index = 0;
        foreach (var group in groups)
        {
            index++;
            var sampleId = newId();
            var condition = group.First().Condition;
            samples.Add(new PlannedSample(
                sampleId,
                barcodeForIndex(index),
                group.Key.SampleTypeId,
                condition?.Name,
                condition?.DelayMinutes));

            foreach (var input in group)
                tests.Add(new PlannedTest(
                    newId(), input.Test.Id, input.Test.Code, sampleId, input.PriceOverride ?? input.Test.Price));
        }

        return new Plan(samples, tests);
    }
}
