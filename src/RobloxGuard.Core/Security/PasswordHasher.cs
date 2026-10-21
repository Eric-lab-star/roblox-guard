using System.Security.Cryptography;

namespace RobloxGuard.Core.Security;

/// <summary>PBKDF2-SHA256 해시 레코드. Salt/Hash 는 base64.</summary>
public sealed record PasswordRecord(string Salt, string Hash, int Iterations);

public static class PasswordHasher
{
    public const int DefaultIterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static PasswordRecord Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, DefaultIterations);
        return new PasswordRecord(Convert.ToBase64String(salt), Convert.ToBase64String(hash), DefaultIterations);
    }

    public static bool Verify(string password, PasswordRecord record)
    {
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(record.Salt);
            expected = Convert.FromBase64String(record.Hash);
        }
        catch (FormatException)
        {
            return false;
        }
        if (record.Iterations <= 0) return false;
        var actual = Derive(password, salt, record.Iterations, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int length = HashBytes)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, length);
}
