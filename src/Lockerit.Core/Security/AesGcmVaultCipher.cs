using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lockerit.Core.Security;

public sealed class AesGcmVaultCipher
{
    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public string EncryptJson<T>(T value, VaultKey key, string purpose)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try
        {
            return EncryptBytes(plaintext, key, purpose);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public T DecryptJson<T>(string encryptedPayload, VaultKey key, string purpose)
    {
        var plaintext = DecryptBytes(encryptedPayload, key, purpose);
        try
        {
            return JsonSerializer.Deserialize<T>(plaintext, JsonOptions)
                ?? throw new InvalidOperationException("The decrypted Lockerit payload was empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string EncryptBytes(byte[] plaintext, VaultKey key, string purpose)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[plaintext.Length];
        var keyMaterial = key.CopyKeyMaterial();

        try
        {
            using var aes = new AesGcm(keyMaterial, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, GetAdditionalAuthenticatedData(purpose));

            var packed = new byte[1 + NonceSize + TagSize + ciphertext.Length];
            packed[0] = FormatVersion;
            Buffer.BlockCopy(nonce, 0, packed, 1, NonceSize);
            Buffer.BlockCopy(tag, 0, packed, 1 + NonceSize, TagSize);
            Buffer.BlockCopy(ciphertext, 0, packed, 1 + NonceSize + TagSize, ciphertext.Length);

            return Convert.ToBase64String(packed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyMaterial);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    private static byte[] DecryptBytes(string encryptedPayload, VaultKey key, string purpose)
    {
        var packed = Convert.FromBase64String(encryptedPayload);
        if (packed.Length < 1 + NonceSize + TagSize || packed[0] != FormatVersion)
        {
            throw new InvalidOperationException("The Lockerit encrypted payload format is not supported.");
        }

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertext = new byte[packed.Length - 1 - NonceSize - TagSize];
        var plaintext = new byte[ciphertext.Length];
        var keyMaterial = key.CopyKeyMaterial();

        try
        {
            Buffer.BlockCopy(packed, 1, nonce, 0, NonceSize);
            Buffer.BlockCopy(packed, 1 + NonceSize, tag, 0, TagSize);
            Buffer.BlockCopy(packed, 1 + NonceSize + TagSize, ciphertext, 0, ciphertext.Length);

            using var aes = new AesGcm(keyMaterial, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, GetAdditionalAuthenticatedData(purpose));

            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyMaterial);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(packed);
        }
    }

    private static byte[] GetAdditionalAuthenticatedData(string purpose)
    {
        return Encoding.UTF8.GetBytes($"Lockerit.AesGcm.v1.{purpose}");
    }
}
