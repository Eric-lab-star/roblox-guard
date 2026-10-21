using System.Security.Cryptography;

namespace RobloxGuard.Core.Security;

/// <summary>PBKDF2-SHA256 해시 레코드. Salt/Hash 는 base64.</summary>
public sealed record PasswordRecord(string Salt, string Hash, int Iterations);

public static class PasswordHasher
{
    public const int DefaultIterations = 100_000;
    public const int MinIterations = 100_000;
    public const int MaxIterations = 2_000_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static PasswordRecord Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, DefaultIterations);
        return new PasswordRecord(Convert.ToBase64String(salt), Convert.ToBase64String(hash), DefaultIterations);
    }

    /// <summary>저장된 레코드가 길이·반복 횟수 규격에 맞지 않으면 무조건 false (fail closed).</summary>
    public static bool Verify(string password, PasswordRecord record)
    {
        if (record is null || string.IsNullOrEmpty(record.Salt) || string.IsNullOrEmpty(record.Hash)) return false;
        if (record.Iterations < MinIterations || record.Iterations > MaxIterations) return false;
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
        if (salt.Length != SaltBytes || expected.Length != HashBytes) return false;
        var actual = Derive(password, salt, record.Iterations, HashBytes);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int length = HashBytes)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, length);
}
