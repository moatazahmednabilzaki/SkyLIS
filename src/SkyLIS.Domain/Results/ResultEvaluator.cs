using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Results;

/// <summary>Outcome of the live rule evaluation for one entered value (P09.1).</summary>
public sealed record ResultEvaluation(
    ResultFlag Flag,
    bool IsCritical,
    bool DeltaFlagged,
    decimal? PreviousValue,
    bool AutoVerify);

/// <summary>
/// Domain service: evaluates an entered value against the test's result schema —
/// absurd-limit guard, reference-range flags, critical limits, and the delta check
/// against the patient's previous result.
/// </summary>
public static class ResultEvaluator
{
    public static ResultEvaluation Evaluate(LabTest test, decimal value, decimal? previousValue)
    {
        var schema = test.ResultSchema
            ?? throw new DomainException($"Test {test.Code} has no result schema; results cannot be entered.");

        // Absurd-value guard: physiologically impossible values cannot be saved (P09.1).
        if ((schema.AbsurdLow is not null && value < schema.AbsurdLow) ||
            (schema.AbsurdHigh is not null && value > schema.AbsurdHigh))
        {
            throw new DomainException(
                $"Value {value} {schema.Unit} for {test.Code} is outside physiologic absurd limits " +
                $"({schema.AbsurdLow}–{schema.AbsurdHigh}); order a rerun or request a supervisor override.");
        }

        var flag = ResultFlag.Normal;
        if (schema.CriticalLow is not null && value <= schema.CriticalLow) flag = ResultFlag.CriticalLow;
        else if (schema.CriticalHigh is not null && value >= schema.CriticalHigh) flag = ResultFlag.CriticalHigh;
        else if (schema.RefLow is not null && value < schema.RefLow) flag = ResultFlag.Low;
        else if (schema.RefHigh is not null && value > schema.RefHigh) flag = ResultFlag.High;

        var deltaFlagged = false;
        if (previousValue is not null && schema.DeltaThresholdPercent is not null && previousValue.Value != 0)
        {
            var deltaPercent = Math.Abs((value - previousValue.Value) / previousValue.Value) * 100m;
            deltaFlagged = deltaPercent > schema.DeltaThresholdPercent.Value;
        }

        var isCritical = flag is ResultFlag.CriticalLow or ResultFlag.CriticalHigh;
        return new ResultEvaluation(flag, isCritical, deltaFlagged, previousValue, schema.AutoVerify);
    }
}
