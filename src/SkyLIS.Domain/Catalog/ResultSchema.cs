using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Catalog;

/// <summary>
/// Numeric result schema for a test (P03.3 Result schema tab, simplified to a single
/// analyte): unit, reference range, critical (panic) limits, absurd physiologic limits,
/// auto-verification eligibility, and the delta-check threshold.
/// Age/sex-banded range sets are a later slice; text/coded results are deferred.
/// </summary>
public sealed class ResultSchema : ValueObject
{
    public string Unit { get; }
    public decimal? RefLow { get; }
    public decimal? RefHigh { get; }
    public decimal? CriticalLow { get; }
    public decimal? CriticalHigh { get; }
    public decimal? AbsurdLow { get; }
    public decimal? AbsurdHigh { get; }
    public bool AutoVerify { get; }
    public decimal? DeltaThresholdPercent { get; }

    private ResultSchema(
        string unit, decimal? refLow, decimal? refHigh, decimal? criticalLow, decimal? criticalHigh,
        decimal? absurdLow, decimal? absurdHigh, bool autoVerify, decimal? deltaThresholdPercent)
    {
        Unit = unit;
        RefLow = refLow;
        RefHigh = refHigh;
        CriticalLow = criticalLow;
        CriticalHigh = criticalHigh;
        AbsurdLow = absurdLow;
        AbsurdHigh = absurdHigh;
        AutoVerify = autoVerify;
        DeltaThresholdPercent = deltaThresholdPercent;
    }

    public static ResultSchema Of(
        string unit, decimal? refLow, decimal? refHigh, decimal? criticalLow, decimal? criticalHigh,
        decimal? absurdLow, decimal? absurdHigh, bool autoVerify, decimal? deltaThresholdPercent)
    {
        if (string.IsNullOrWhiteSpace(unit)) throw new DomainException("Result unit is required.");
        if (refLow is not null && refHigh is not null && refLow > refHigh)
            throw new DomainException("Reference range low must not exceed high.");
        if (criticalLow is not null && refLow is not null && criticalLow > refLow)
            throw new DomainException("Critical low must not exceed the reference low.");
        if (criticalHigh is not null && refHigh is not null && criticalHigh < refHigh)
            throw new DomainException("Critical high must not be below the reference high.");
        if (absurdLow is not null && absurdHigh is not null && absurdLow > absurdHigh)
            throw new DomainException("Absurd limits are inverted.");
        if (deltaThresholdPercent is <= 0 or > 1000)
            throw new DomainException("Delta threshold must be between 0 and 1000 percent.");
        return new ResultSchema(unit.Trim(), refLow, refHigh, criticalLow, criticalHigh,
            absurdLow, absurdHigh, autoVerify, deltaThresholdPercent);
    }

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return Unit; yield return RefLow; yield return RefHigh;
        yield return CriticalLow; yield return CriticalHigh;
        yield return AbsurdLow; yield return AbsurdHigh;
        yield return AutoVerify; yield return DeltaThresholdPercent;
    }
}
