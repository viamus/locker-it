using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace Lockerit.Core.Security;

public static class TotpAuthenticator
{
    public const int DefaultAllowedDriftSteps = 2;
    public const int EnrollmentAllowedDriftSteps = 4;
    public const int AuthPolicyAllowedDriftSteps = 4;
    private const int SecretByteLength = 20;
    private const int CodeDigits = 6;
    private const int TimeStepSeconds = 30;

    public static string CreateSecret()
    {
        Span<byte> secret = stackalloc byte[SecretByteLength];
        RandomNumberGenerator.Fill(secret);
        return Base32.Encode(secret);
    }

    public static string CreateSetupUri(string issuer, string accountName, string secretBase32)
    {
        var label = $"{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}";
        return $"otpauth://totp/{label}?secret={Uri.EscapeDataString(secretBase32)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={CodeDigits}&period={TimeStepSeconds}";
    }

    public static string GenerateCode(string secretBase32, DateTimeOffset timestamp)
    {
        var secret = Base32.Decode(secretBase32);
        try
        {
            var counter = timestamp.ToUnixTimeSeconds() / TimeStepSeconds;
            return GenerateCode(secret, counter);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public static bool VerifyCode(
        string secretBase32,
        string code,
        DateTimeOffset timestamp,
        int allowedDriftSteps = DefaultAllowedDriftSteps)
    {
        var normalizedCode = NormalizeCode(code);
        if (normalizedCode.Length != CodeDigits)
        {
            return false;
        }

        var secret = Base32.Decode(secretBase32);
        try
        {
            var counter = timestamp.ToUnixTimeSeconds() / TimeStepSeconds;
            for (var offset = -allowedDriftSteps; offset <= allowedDriftSteps; offset++)
            {
                var expected = GenerateCode(secret, counter + offset);
                if (CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.ASCII.GetBytes(expected),
                        System.Text.Encoding.ASCII.GetBytes(normalizedCode)))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public static string NormalizeCode(string code)
    {
        return new string(code.Where(char.IsDigit).ToArray());
    }

    private static string GenerateCode(ReadOnlySpan<byte> secret, long counter)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        Span<byte> hash = stackalloc byte[32];
        var secretKey = secret.ToArray();
        try
        {
            using var hmac = new HMACSHA1(secretKey);
            var written = hmac.TryComputeHash(counterBytes, hash, out var bytesWritten);
            if (!written || bytesWritten < 20)
            {
                throw new CryptographicException("Could not generate the authenticator code.");
            }

            var offset = hash[bytesWritten - 1] & 0x0F;
            var binaryCode =
                ((hash[offset] & 0x7F) << 24) |
                ((hash[offset + 1] & 0xFF) << 16) |
                ((hash[offset + 2] & 0xFF) << 8) |
                (hash[offset + 3] & 0xFF);

            var otp = binaryCode % 1_000_000;
            return otp.ToString("D6", CultureInfo.InvariantCulture);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretKey);
        }
    }
}
