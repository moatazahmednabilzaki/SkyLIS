using System.Security.Cryptography;
using SkyLIS.Application.Users;

namespace SkyLIS.Infrastructure.Auth;

/// <summary>
/// RFC 6238 TOTP (SHA-1, 30-second step, 6 digits — the profile every authenticator app
/// implements). Verification accepts ±1 step of clock drift and compares in fixed time.
/// </summary>
internal sealed class TotpService : ITotpService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int StepSeconds = 30;
    private const int Digits = 6;

    public string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        var result = new System.Text.StringBuilder();
        var bitBuffer = 0;
        var bitCount = 0;
        foreach (var b in bytes)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;
            while (bitCount >= 5)
            {
                result.Append(Base32Alphabet[(bitBuffer >> (bitCount - 5)) & 0x1F]);
                bitCount -= 5;
            }
        }
        if (bitCount > 0)
            result.Append(Base32Alphabet[(bitBuffer << (5 - bitCount)) & 0x1F]);
        return result.ToString();
    }

    public bool Verify(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != Digits || !code.All(char.IsDigit))
            return false;
        var key = Base32Decode(secret);
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;
        for (var offset = -1; offset <= 1; offset++)
        {
            var expected = ComputeCode(key, step + offset);
            if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(expected),
                    System.Text.Encoding.ASCII.GetBytes(code)))
            {
                return true;
            }
        }
        return false;
    }

    public string BuildOtpAuthUri(string secret, string account, string issuer) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}"
        + $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={StepSeconds}";

    internal static string ComputeCode(byte[] key, long step)
    {
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | (hash[offset + 1] << 16)
                     | (hash[offset + 2] << 8)
                     | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    internal static byte[] Base32Decode(string secret)
    {
        var bits = 0;
        var bitCount = 0;
        var bytes = new List<byte>();
        foreach (var c in secret.Trim().ToUpperInvariant().Where(c => c != '='))
        {
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0) throw new FormatException("Invalid base32 character in the MFA secret.");
            bits = (bits << 5) | index;
            bitCount += 5;
            if (bitCount >= 8)
            {
                bytes.Add((byte)((bits >> (bitCount - 8)) & 0xFF));
                bitCount -= 8;
            }
        }
        return [.. bytes];
    }
}
