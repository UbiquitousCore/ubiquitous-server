using System.Security.Cryptography;
using System.Text;

namespace Ubiquitous.Persistence;

public static class PasswordHasher
{
    public const string Scheme = "pbkdf2-sha256";
    public const int DefaultIterations = 600_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const char Separator = '$';

    public static string Hash(string secret, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Join(Separator, Scheme, iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool IsHashed(string stored) => stored.StartsWith(Scheme + Separator, StringComparison.Ordinal);

    public static bool Verify(string secret, string stored)
    {
        if (!IsHashed(stored)) return SameText(secret, stored);
        var parts = stored.Split(Separator);
        if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations < 1) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool SameText(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}
