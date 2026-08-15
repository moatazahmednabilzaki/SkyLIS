namespace SkyLIS.Domain.Common;

/// <summary>
/// Mobile number in E.164 form. Country-pack-specific pattern validation happens in the
/// Application layer (the pack is configuration); the domain guarantees canonical shape.
/// </summary>
public sealed class PhoneNumber : ValueObject
{
    public string Value { get; }

    private PhoneNumber(string value) => Value = value;

    public static PhoneNumber Of(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("Phone number is required.");

        var digits = new string(raw.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (digits.StartsWith('+')) digits = "+" + new string(digits.Skip(1).Where(char.IsDigit).ToArray());

        if (digits.Length is < 8 or > 16)
            throw new DomainException("Phone number must contain 8-15 digits.");

        return new PhoneNumber(digits);
    }

    protected override IEnumerable<object?> EqualityComponents() { yield return Value; }

    public override string ToString() => Value;
}
