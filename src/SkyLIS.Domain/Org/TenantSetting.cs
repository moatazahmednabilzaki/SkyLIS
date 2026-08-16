using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Org;

/// <summary>
/// Tenant-owned aggregate (FR-SYS-004): one configuration value. Well-known keys:
/// report.headerNameOverride, report.footerNote, report.footerNoteAr,
/// rejection.reasons (comma-separated coded vocabulary enforced at P07.3).
/// </summary>
public sealed class TenantSetting : AggregateRoot, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Key { get; private set; } = null!;
    public string Value { get; private set; } = null!;
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private TenantSetting() { } // EF

    public static TenantSetting Create(Guid id, Guid tenantId, string key, string value, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        var normalized = key?.Trim().ToLowerInvariant() ?? "";
        if (normalized.Length is < 3 or > 80 || !normalized.All(c => char.IsLetterOrDigit(c) || c is '.' or '-'))
            throw new DomainException("Setting key: 3–80 characters of letters, digits, dot, or dash.");
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000)
            throw new DomainException("Setting value: 1–2000 characters.");
        return new TenantSetting
        {
            Id = id,
            TenantId = tenantId,
            Key = normalized,
            Value = value.Trim(),
            UpdatedAtUtc = nowUtc,
        };
    }

    public void Update(string value, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000)
            throw new DomainException("Setting value: 1–2000 characters.");
        Value = value.Trim();
        UpdatedAtUtc = nowUtc;
    }
}
