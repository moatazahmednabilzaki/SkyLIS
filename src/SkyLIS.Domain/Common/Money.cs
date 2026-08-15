namespace SkyLIS.Domain.Common;

/// <summary>Monetary amount with currency. Amounts are stored as decimal; never floats.</summary>
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Of(decimal amount, string currency)
    {
        if (amount < 0) throw new DomainException("Money amount cannot be negative.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new DomainException("Currency must be a 3-letter ISO code.");
        return new Money(decimal.Round(amount, 2), currency.ToUpperInvariant());
    }

    public static Money Zero(string currency) => Of(0, currency);

    public Money Add(Money other) => Combine(other, (a, b) => a + b);

    public Money Subtract(Money other)
    {
        var result = Combine(other, (a, b) => a - b);
        return result.Amount < 0
            ? throw new DomainException("Resulting money amount cannot be negative.")
            : result;
    }

    private Money Combine(Money other, Func<decimal, decimal, decimal> op)
    {
        if (Currency != other.Currency)
            throw new DomainException($"Cannot combine {Currency} with {other.Currency}.");
        return new Money(op(Amount, other.Amount), Currency);
    }

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
