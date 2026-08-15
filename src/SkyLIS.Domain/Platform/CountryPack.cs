using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Platform;

/// <summary>One default pre-analytic condition shipped inside a country pack.</summary>
public sealed record PackCondition(string Name, int? DelayMinutes, string CompatibilityGroup);

/// <summary>One default sample type (with its condition tree) shipped inside a country pack.</summary>
public sealed record PackSampleType(string Name, string ContainerName, IReadOnlyList<PackCondition> Conditions);

/// <summary>
/// Platform-owned aggregate (P01.4 / FR-TEN-040): country default pack. When a tenant is
/// provisioned, the pack matching its country code seeds the tenant's sample taxonomy
/// (via the TenantProvisioned outbox consumer) so day one starts configured, not blank.
/// </summary>
public sealed class CountryPack : AggregateRoot
{
    public string CountryCode { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public int Version { get; private set; }
    public IReadOnlyList<PackSampleType> SampleTypes { get; private set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private CountryPack() { } // EF

    public static CountryPack Create(
        Guid id, string countryCode, string name, string currency,
        IReadOnlyList<PackSampleType> sampleTypes, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Trim().Length != 2)
            throw new DomainException("Country code must be an ISO 3166-1 alpha-2 code.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Country pack name is required.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            throw new DomainException("Currency must be an ISO 4217 code.");

        var pack = new CountryPack
        {
            Id = id,
            CountryCode = countryCode.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Currency = currency.Trim().ToUpperInvariant(),
            Version = 1,
            UpdatedAtUtc = nowUtc,
        };
        pack.SetContent(sampleTypes);
        return pack;
    }

    /// <summary>Replaces the pack content and bumps the version (new tenants get the new content).</summary>
    public void ReplaceContent(string name, string currency, IReadOnlyList<PackSampleType> sampleTypes, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Country pack name is required.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            throw new DomainException("Currency must be an ISO 4217 code.");
        Name = name.Trim();
        Currency = currency.Trim().ToUpperInvariant();
        SetContent(sampleTypes);
        Version++;
        UpdatedAtUtc = nowUtc;
    }

    private void SetContent(IReadOnlyList<PackSampleType> sampleTypes)
    {
        if (sampleTypes.Count == 0)
            throw new DomainException("A country pack requires at least one default sample type.");
        if (sampleTypes.Any(s => string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.ContainerName)))
            throw new DomainException("Every pack sample type requires a name and a container.");
        if (sampleTypes.SelectMany(s => s.Conditions).Any(c =>
                string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.CompatibilityGroup)
                || c.DelayMinutes is < 0 or > 24 * 60))
            throw new DomainException("Every pack condition requires a name, a compatibility group, and a delay within 24h.");
        SampleTypes = sampleTypes;
    }
}
