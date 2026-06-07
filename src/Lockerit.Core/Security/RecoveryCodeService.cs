using System.Security.Cryptography;
using System.Text;

namespace Lockerit.Core.Security;

internal static class RecoveryCodeService
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int SaltLength = 16;

    public static IReadOnlyList<string> GenerateCodes(int count)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        while (codes.Count < count)
        {
            codes.Add($"{RandomToken(5)}-{RandomToken(5)}");
        }

        return codes.ToArray();
    }

    public static RecoveryCodeHash HashCode(string code)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        byte[]? hash = null;
        try
        {
            hash = HashNormalizedCode(code, salt);
            return new RecoveryCodeHash(
                Convert.ToBase64String(salt),
                Convert.ToBase64String(hash),
                DateTimeOffset.UtcNow,
                UsedAtUtc: null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            if (hash is not null)
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }
    }

    public static bool Verify(string code, RecoveryCodeHash expected)
    {
        var salt = Convert.FromBase64String(expected.SaltBase64);
        var expectedHash = Convert.FromBase64String(expected.HashBase64);
        try
        {
            var actualHash = HashNormalizedCode(code, salt);
            try
            {
                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actualHash);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expectedHash);
        }
    }

    private static byte[] HashNormalizedCode(string code, ReadOnlySpan<byte> salt)
    {
        var normalized = Encoding.UTF8.GetBytes(Normalize(code));
        var buffer = new byte[salt.Length + normalized.Length];
        try
        {
            salt.CopyTo(buffer);
            normalized.CopyTo(buffer.AsSpan(salt.Length));
            return SHA256.HashData(buffer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(normalized);
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static string Normalize(string code)
    {
        return new string(code
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string RandomToken(int length)
    {
        Span<char> chars = stackalloc char[length];
        for (var index = 0; index < length; index++)
        {
            chars[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }
}
