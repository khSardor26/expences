using System.Security.Cryptography;
using System.Text;

namespace MyExpenses.Api.Security;

/// <summary>Random codes, all from the cryptographic random number generator.</summary>
public static class SecurityCodes
{
    // No 0/O or 1/I, so a code copied from paper can't be misread.
    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string NewCode() => RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");

    /// <summary>Only letters, digits, - and _, which is all Telegram allows in a start link.</summary>
    public static string NewLinkToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>For example K7MQ2-XR4TN.</summary>
    public static string NewRecoveryCode()
    {
        var code = RandomNumberGenerator.GetString(RecoveryAlphabet, 10);
        return $"{code[..5]}-{code[5..]}";
    }

    /// <summary>Case and dashes don't matter when typing a recovery code.</summary>
    public static string NormalizeRecoveryCode(string code) =>
        code.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();

    /// <summary>
    /// For long random values only, where a plain hash is safe. The short 6-digit
    /// codes go through the slow password hasher instead.
    /// </summary>
    public static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
