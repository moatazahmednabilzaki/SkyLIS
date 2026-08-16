using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Catalog;

/// <summary>
/// Tenant-owned aggregate (P03.5): a panel/profile — a bundle of tests ordered as one
/// item at a bundle price. Ordering a panel expands to its member tests; the panel price
/// is allocated across the member lines (equal split, remainder on the first line) so
/// invoice totals always equal the advertised bundle price.
/// </summary>
public sealed class Panel : AggregateRoot, ITenantOwned
{
    private readonly List<PanelItem> _items = [];

    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public Money Price { get; private set; } = null!;
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<PanelItem> Items => _items.AsReadOnly();

    private Panel() { } // EF

    public static Panel Create(
        Guid id, Guid tenantId, string code, string name, Money price, IReadOnlyList<Guid> testIds)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Panel code is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Panel name is required.");
        if (price.Amount <= 0) throw new DomainException("Panel price must be positive.");
        if (testIds.Count < 2)
            throw new DomainException("A panel bundles at least two tests — otherwise order the test directly.");
        if (testIds.Distinct().Count() != testIds.Count)
            throw new DomainException("A panel cannot contain the same test twice.");

        var panel = new Panel
        {
            Id = id,
            TenantId = tenantId,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Price = price,
            IsActive = true,
        };
        foreach (var testId in testIds)
            panel._items.Add(new PanelItem(Guid.CreateVersion7(), tenantId, panel.Id, testId));
        return panel;
    }

    /// <summary>Equal split of the bundle price across members; the remainder lands on the first line.</summary>
    public IReadOnlyList<Money> AllocatePrice()
    {
        var count = _items.Count;
        var per = decimal.Round(Price.Amount / count, 2, MidpointRounding.ToZero);
        var first = Price.Amount - per * (count - 1);
        return Enumerable.Range(0, count)
            .Select(i => Money.Of(i == 0 ? first : per, Price.Currency))
            .ToList();
    }

    public void Retire() => IsActive = false;
}

public sealed class PanelItem : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid PanelId { get; private set; }
    public Guid TestId { get; private set; }

    private PanelItem() { } // EF

    internal PanelItem(Guid id, Guid tenantId, Guid panelId, Guid testId) : base(id)
    {
        TenantId = tenantId;
        PanelId = panelId;
        TestId = testId;
    }
}
