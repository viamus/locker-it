using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lockerit.Core.Storage;

namespace Lockerit.Core.Security;

public sealed class WindowsProtectedKeyStore
{
    private const string KeyHeaderV1 = "LockeritKey.v1.";
    private const string KeyHeaderV2 = "LockeritKey.v2.";
    private const string KeyringFormat = "Lockerit.LocalKeyring";
    private const string ProtectionModeMasterPassword = "DPAPI-CurrentUser+MasterPassword";
    private const string KdfName = "PBKDF2-HMAC-SHA256";
    private const string CipherName = "AES-256-GCM";
    private const int DefaultIterations = 600_000;
    private const int MinimumIterations = 100_000;
    private const int MaximumIterations = 5_000_000;
    private const int MinimumMasterPasswordLength = 12;
    private const int SaltSize = 32;
    private const int WrappingKeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly byte[] MasterPasswordAdditionalAuthenticatedData =
        Encoding.UTF8.GetBytes("Lockerit.LocalKeyring.v2.MasterPasswordWrap");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly LockeritPaths _paths;

    public WindowsProtectedKeyStore(LockeritPaths paths)
    {
        _paths = paths;
    }

    public bool KeyFileExists => File.Exists(_paths.KeyFilePath);

    public KeyOpenResult OpenOrCreate(string? masterPassword = null)
    {
        Directory.CreateDirectory(_paths.RootDirectory);

        if (File.Exists(_paths.KeyFilePath))
        {
            return new KeyOpenResult(new VaultKey(ReadProtectedKey(masterPassword)), CreatedNewKey: false);
        }

        var rawKey = RandomNumberGenerator.GetBytes(VaultKey.SizeInBytes);
        try
        {
            WriteProtectedKey(rawKey);
            return new KeyOpenResult(new VaultKey((byte[])rawKey.Clone()), CreatedNewKey: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawKey);
        }
    }

    public KeyProtectionMode GetProtectionMode()
    {
        if (!File.Exists(_paths.KeyFilePath))
        {
            return KeyProtectionMode.WindowsUser;
        }

        var encodedKey = File.ReadAllText(_paths.KeyFilePath, Encoding.UTF8).Trim();
        if (encodedKey.StartsWith(KeyHeaderV2, StringComparison.Ordinal))
        {
            return KeyProtectionMode.WindowsUserWithMasterPassword;
        }

        if (encodedKey.StartsWith(KeyHeaderV1, StringComparison.Ordinal))
        {
            return KeyProtectionMode.WindowsUser;
        }

        throw new InvalidOperationException("The Lockerit key file format is not supported.");
    }

    public void SaveImportedKey(ReadOnlySpan<byte> rawKey)
    {
        if (rawKey.Length != VaultKey.SizeInBytes)
        {
            throw new ArgumentException($"The imported Lockerit vault key must be {VaultKey.SizeInBytes} bytes.", nameof(rawKey));
        }

        Directory.CreateDirectory(_paths.RootDirectory);
        WriteProtectedKey(rawKey);
    }

    public void SaveImportedKeyWithMasterPassword(ReadOnlySpan<byte> rawKey, string masterPassword)
    {
        if (rawKey.Length != VaultKey.SizeInBytes)
        {
            throw new ArgumentException($"The imported Lockerit vault key must be {VaultKey.SizeInBytes} bytes.", nameof(rawKey));
        }

        ValidateMasterPassword(masterPassword);
        Directory.CreateDirectory(_paths.RootDirectory);
        WriteProtectedKeyWithMasterPassword(rawKey, masterPassword);
    }

    private void WriteProtectedKey(ReadOnlySpan<byte> rawKey)
    {
        var keyCopy = rawKey.ToArray();
        byte[]? protectedKey = null;

        try
        {
            protectedKey = ProtectedData.Protect(keyCopy, GetAdditionalEntropy(), DataProtectionScope.CurrentUser);
            WriteEncodedKey(KeyHeaderV1, protectedKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyCopy);
            if (protectedKey is not null)
            {
                CryptographicOperations.ZeroMemory(protectedKey);
            }
        }
    }

    private void WriteProtectedKeyWithMasterPassword(ReadOnlySpan<byte> rawKey, string masterPassword)
    {
        var keyCopy = rawKey.ToArray();
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[VaultKey.SizeInBytes];
        var wrappingKey = DeriveWrappingKey(masterPassword, salt, DefaultIterations);
        byte[]? documentBytes = null;
        byte[]? protectedDocument = null;

        try
        {
            using var aes = new AesGcm(wrappingKey, TagSize);
            aes.Encrypt(nonce, keyCopy, ciphertext, tag, MasterPasswordAdditionalAuthenticatedData);

            var document = new KeyringDocument
            {
                Format = KeyringFormat,
                Version = 2,
                ProtectionMode = ProtectionModeMasterPassword,
                Kdf = new KeyringKdf
                {
                    Name = KdfName,
                    Iterations = DefaultIterations,
                    Salt = Convert.ToBase64String(salt)
                },
                Cipher = new KeyringCipher
                {
                    Name = CipherName,
                    Nonce = Convert.ToBase64String(nonce),
                    Tag = Convert.ToBase64String(tag),
                    Ciphertext = Convert.ToBase64String(ciphertext)
                }
            };

            documentBytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            protectedDocument = ProtectedData.Protect(documentBytes, GetAdditionalEntropy(), DataProtectionScope.CurrentUser);
            WriteEncodedKey(KeyHeaderV2, protectedDocument);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyCopy);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(wrappingKey);
            if (documentBytes is not null)
            {
                CryptographicOperations.ZeroMemory(documentBytes);
            }

            if (protectedDocument is not null)
            {
                CryptographicOperations.ZeroMemory(protectedDocument);
            }
        }
    }

    private void WriteEncodedKey(string header, byte[] protectedKey)
    {
        var encodedKey = header + Convert.ToBase64String(protectedKey);
        var temporaryPath = _paths.KeyFilePath + ".tmp";

        File.WriteAllText(temporaryPath, encodedKey, Encoding.UTF8);
        File.Move(temporaryPath, _paths.KeyFilePath, overwrite: true);
    }

    private byte[] ReadProtectedKey(string? masterPassword)
    {
        var encodedKey = File.ReadAllText(_paths.KeyFilePath, Encoding.UTF8).Trim();
        if (encodedKey.StartsWith(KeyHeaderV1, StringComparison.Ordinal))
        {
            return ReadV1ProtectedKey(encodedKey);
        }

        if (encodedKey.StartsWith(KeyHeaderV2, StringComparison.Ordinal))
        {
            return ReadV2ProtectedKey(encodedKey, masterPassword);
        }

        throw new InvalidOperationException("The Lockerit key file format is not supported.");
    }

    private static byte[] ReadV1ProtectedKey(string encodedKey)
    {
        var protectedKey = Convert.FromBase64String(encodedKey[KeyHeaderV1.Length..]);
        try
        {
            var rawKey = ProtectedData.Unprotect(protectedKey, GetAdditionalEntropy(), DataProtectionScope.CurrentUser);
            ValidateRawKey(rawKey);
            return rawKey;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedKey);
        }
    }

    private static byte[] ReadV2ProtectedKey(string encodedKey, string? masterPassword)
    {
        if (string.IsNullOrWhiteSpace(masterPassword))
        {
            throw new VaultMasterPasswordRequiredException();
        }

        var protectedDocument = Convert.FromBase64String(encodedKey[KeyHeaderV2.Length..]);
        byte[]? documentBytes = null;
        byte[]? salt = null;
        byte[]? nonce = null;
        byte[]? tag = null;
        byte[]? ciphertext = null;
        byte[]? wrappingKey = null;
        var rawKey = new byte[VaultKey.SizeInBytes];

        try
        {
            documentBytes = ProtectedData.Unprotect(protectedDocument, GetAdditionalEntropy(), DataProtectionScope.CurrentUser);
            var document = JsonSerializer.Deserialize<KeyringDocument>(documentBytes, JsonOptions)
                ?? throw new InvalidOperationException("The Lockerit key file is empty.");
            ValidateDocument(document);

            salt = Convert.FromBase64String(document.Kdf.Salt);
            nonce = Convert.FromBase64String(document.Cipher.Nonce);
            tag = Convert.FromBase64String(document.Cipher.Tag);
            ciphertext = Convert.FromBase64String(document.Cipher.Ciphertext);
            wrappingKey = DeriveWrappingKey(masterPassword, salt, document.Kdf.Iterations);

            using var aes = new AesGcm(wrappingKey, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, rawKey, MasterPasswordAdditionalAuthenticatedData);
            ValidateRawKey(rawKey);
            return rawKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(rawKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedDocument);
            ZeroIfNotNull(documentBytes);
            ZeroIfNotNull(salt);
            ZeroIfNotNull(nonce);
            ZeroIfNotNull(tag);
            ZeroIfNotNull(ciphertext);
            ZeroIfNotNull(wrappingKey);
        }
    }

    private static byte[] DeriveWrappingKey(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            WrappingKeySize);
    }

    private static void ValidateMasterPassword(string masterPassword)
    {
        if (string.IsNullOrWhiteSpace(masterPassword) || masterPassword.Length < MinimumMasterPasswordLength)
        {
            throw new ArgumentException($"The master password must be at least {MinimumMasterPasswordLength} characters.", nameof(masterPassword));
        }
    }

    private static void ValidateRawKey(byte[] rawKey)
    {
        if (rawKey.Length != VaultKey.SizeInBytes)
        {
            CryptographicOperations.ZeroMemory(rawKey);
            throw new InvalidOperationException("The Lockerit key file is invalid.");
        }
    }

    private static void ValidateDocument(KeyringDocument document)
    {
        if (!string.Equals(document.Format, KeyringFormat, StringComparison.Ordinal) ||
            document.Version != 2 ||
            !string.Equals(document.ProtectionMode, ProtectionModeMasterPassword, StringComparison.Ordinal) ||
            document.Kdf is null ||
            document.Cipher is null)
        {
            throw new InvalidOperationException("The Lockerit key file format is not supported.");
        }

        if (!string.Equals(document.Kdf.Name, KdfName, StringComparison.Ordinal) ||
            document.Kdf.Iterations is < MinimumIterations or > MaximumIterations)
        {
            throw new InvalidOperationException("The Lockerit key file KDF parameters are not supported.");
        }

        if (!string.Equals(document.Cipher.Name, CipherName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Lockerit key file cipher is not supported.");
        }
    }

    private static void ZeroIfNotNull(byte[]? buffer)
    {
        if (buffer is not null)
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static byte[] GetAdditionalEntropy()
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes("Lockerit.LocalVault.MasterKey.v1"));
    }

    private sealed class KeyringDocument
    {
        public string Format { get; set; } = string.Empty;
        public int Version { get; set; }
        public string ProtectionMode { get; set; } = string.Empty;
        public KeyringKdf Kdf { get; set; } = new();
        public KeyringCipher Cipher { get; set; } = new();
    }

    private sealed class KeyringKdf
    {
        public string Name { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public string Salt { get; set; } = string.Empty;
    }

    private sealed class KeyringCipher
    {
        public string Name { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Ciphertext { get; set; } = string.Empty;
    }
}
