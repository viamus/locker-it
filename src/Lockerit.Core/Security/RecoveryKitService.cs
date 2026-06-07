using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lockerit.Core.Security;

internal sealed class RecoveryKitService
{
    private const string FormatName = "Lockerit.RecoveryKit";
    private const int FormatVersion = 1;
    private const string KdfName = "PBKDF2-HMAC-SHA256";
    private const string CipherName = "AES-256-GCM";
    private const int DefaultIterations = 600_000;
    private const int MinimumIterations = 100_000;
    private const int MaximumIterations = 5_000_000;
    private const int SaltSize = 32;
    private const int WrappingKeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MinimumPassphraseLength = 12;
    private const long MaximumRecoveryKitBytes = 1024 * 1024;

    private static readonly byte[] AdditionalAuthenticatedData =
        Encoding.UTF8.GetBytes("Lockerit.RecoveryKit.v1.MasterKeyWrap");

    private static readonly byte[] FingerprintPurpose =
        Encoding.UTF8.GetBytes("Lockerit.RecoveryKit.KeyFingerprint.v1");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public RecoveryKitExportResult Export(string recoveryKitPath, ReadOnlySpan<byte> vaultKey, string passphrase, string? passphraseHint)
    {
        if (vaultKey.Length != VaultKey.SizeInBytes)
        {
            throw new ArgumentException($"The Lockerit vault key must be {VaultKey.SizeInBytes} bytes.", nameof(vaultKey));
        }

        ValidateExportPassphrase(passphrase);

        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(recoveryKitPath));
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The recovery kit path must include a directory.", nameof(recoveryKitPath));
        }

        Directory.CreateDirectory(directory);

        var createdAtUtc = DateTimeOffset.UtcNow;
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[VaultKey.SizeInBytes];
        var wrappingKey = DeriveWrappingKey(passphrase, salt, DefaultIterations);
        var vaultKeyCopy = vaultKey.ToArray();

        try
        {
            using var aes = new AesGcm(wrappingKey, TagSize);
            aes.Encrypt(nonce, vaultKeyCopy, ciphertext, tag, AdditionalAuthenticatedData);

            var fingerprint = CreateFingerprint(vaultKeyCopy);
            try
            {
                var document = new RecoveryKitDocument
                {
                    Format = FormatName,
                    Version = FormatVersion,
                    CreatedAtUtc = createdAtUtc,
                    Kdf = new RecoveryKitKdf
                    {
                        Name = KdfName,
                        Iterations = DefaultIterations,
                        Salt = Convert.ToBase64String(salt)
                    },
                    Cipher = new RecoveryKitCipher
                    {
                        Name = CipherName,
                        Nonce = Convert.ToBase64String(nonce),
                        Tag = Convert.ToBase64String(tag),
                        Ciphertext = Convert.ToBase64String(ciphertext)
                    },
                    KeyFingerprint = Convert.ToBase64String(fingerprint),
                    PassphraseHint = NormalizeHint(passphraseHint)
                };

                var temporaryPath = fullPath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions), Encoding.UTF8);
                File.Move(temporaryPath, fullPath, overwrite: true);

                return new RecoveryKitExportResult(
                    fullPath,
                    createdAtUtc,
                    Convert.ToBase64String(fingerprint));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(fingerprint);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(vaultKeyCopy);
        }
    }

    public RecoveredVaultKey Import(string recoveryKitPath, string passphrase)
    {
        ValidateImportPassphrase(passphrase);

        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(recoveryKitPath));
        var document = ReadDocument(fullPath);

        ValidateDocument(document);

        var salt = Convert.FromBase64String(document.Kdf.Salt);
        var nonce = Convert.FromBase64String(document.Cipher.Nonce);
        var tag = Convert.FromBase64String(document.Cipher.Tag);
        var ciphertext = Convert.FromBase64String(document.Cipher.Ciphertext);
        var expectedFingerprint = Convert.FromBase64String(document.KeyFingerprint);
        var wrappingKey = DeriveWrappingKey(passphrase, salt, document.Kdf.Iterations);
        var vaultKey = new byte[VaultKey.SizeInBytes];

        try
        {
            using var aes = new AesGcm(wrappingKey, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, vaultKey, AdditionalAuthenticatedData);

            var actualFingerprint = CreateFingerprint(vaultKey);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(expectedFingerprint, actualFingerprint))
                {
                    throw new CryptographicException("The Lockerit Recovery Kit key fingerprint does not match.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actualFingerprint);
            }

            return new RecoveredVaultKey(
                vaultKey,
                new RecoveryKitImportResult(
                    fullPath,
                    document.CreatedAtUtc,
                    Convert.ToBase64String(expectedFingerprint)));
        }
        catch
        {
            CryptographicOperations.ZeroMemory(vaultKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(expectedFingerprint);
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    public RecoveryKitMetadata ReadMetadata(string recoveryKitPath)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(recoveryKitPath));
        var document = ReadDocument(fullPath);
        ValidateDocument(document);

        return new RecoveryKitMetadata(
            fullPath,
            document.CreatedAtUtc,
            string.IsNullOrWhiteSpace(document.PassphraseHint) ? null : document.PassphraseHint,
            document.Kdf.Name,
            document.Kdf.Iterations);
    }

    private static byte[] DeriveWrappingKey(string passphrase, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            WrappingKeySize);
    }

    private static byte[] CreateFingerprint(byte[] vaultKey)
    {
        using var hmac = new HMACSHA256(vaultKey);
        return hmac.ComputeHash(FingerprintPurpose);
    }

    private static RecoveryKitDocument ReadDocument(string fullPath)
    {
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("The Lockerit Recovery Kit was not found.", fullPath);
        }

        if (fileInfo.Length > MaximumRecoveryKitBytes)
        {
            throw new InvalidOperationException("The Lockerit Recovery Kit file is too large.");
        }

        return JsonSerializer.Deserialize<RecoveryKitDocument>(File.ReadAllText(fullPath, Encoding.UTF8), JsonOptions)
            ?? throw new InvalidOperationException("The Lockerit Recovery Kit is empty.");
    }

    private static string? NormalizeHint(string? passphraseHint)
    {
        var normalized = passphraseHint?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void ValidateExportPassphrase(string passphrase)
    {
        ValidateImportPassphrase(passphrase);

        if (passphrase.Length < MinimumPassphraseLength)
        {
            throw new ArgumentException($"The recovery passphrase must be at least {MinimumPassphraseLength} characters.", nameof(passphrase));
        }
    }

    private static void ValidateImportPassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            throw new ArgumentException("The recovery passphrase is required.", nameof(passphrase));
        }
    }

    private static void ValidateDocument(RecoveryKitDocument document)
    {
        if (!string.Equals(document.Format, FormatName, StringComparison.Ordinal) ||
            document.Version != FormatVersion)
        {
            throw new InvalidOperationException("The Lockerit Recovery Kit format is not supported.");
        }

        if (document.Kdf is null || document.Cipher is null)
        {
            throw new InvalidOperationException("The Lockerit Recovery Kit is incomplete.");
        }

        if (!string.Equals(document.Kdf.Name, KdfName, StringComparison.Ordinal) ||
            document.Kdf.Iterations is < MinimumIterations or > MaximumIterations)
        {
            throw new InvalidOperationException("The Lockerit Recovery Kit KDF parameters are not supported.");
        }

        if (!string.Equals(document.Cipher.Name, CipherName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Lockerit Recovery Kit cipher is not supported.");
        }

        ValidateBase64Size(document.Kdf.Salt, SaltSize, "salt");
        ValidateBase64Size(document.Cipher.Nonce, NonceSize, "nonce");
        ValidateBase64Size(document.Cipher.Tag, TagSize, "authentication tag");
        ValidateBase64Size(document.Cipher.Ciphertext, VaultKey.SizeInBytes, "ciphertext");
        ValidateBase64Size(document.KeyFingerprint, SHA256.HashSizeInBytes, "key fingerprint");
    }

    private static void ValidateBase64Size(string value, int expectedLength, string fieldName)
    {
        try
        {
            var decoded = Convert.FromBase64String(value);
            try
            {
                if (decoded.Length != expectedLength)
                {
                    throw new InvalidOperationException($"The Lockerit Recovery Kit {fieldName} is invalid.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decoded);
            }
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"The Lockerit Recovery Kit {fieldName} is invalid.", ex);
        }
        catch (ArgumentNullException ex)
        {
            throw new InvalidOperationException($"The Lockerit Recovery Kit {fieldName} is invalid.", ex);
        }
    }

    private sealed class RecoveryKitDocument
    {
        public string Format { get; set; } = string.Empty;
        public int Version { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public RecoveryKitKdf Kdf { get; set; } = new();
        public RecoveryKitCipher Cipher { get; set; } = new();
        public string KeyFingerprint { get; set; } = string.Empty;
        public string? PassphraseHint { get; set; }
    }

    private sealed class RecoveryKitKdf
    {
        public string Name { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public string Salt { get; set; } = string.Empty;
    }

    private sealed class RecoveryKitCipher
    {
        public string Name { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Ciphertext { get; set; } = string.Empty;
    }
}

internal sealed class RecoveredVaultKey : IDisposable
{
    public RecoveredVaultKey(byte[] keyMaterial, RecoveryKitImportResult importResult)
    {
        if (keyMaterial.Length != VaultKey.SizeInBytes)
        {
            throw new ArgumentException($"The recovered Lockerit vault key must be {VaultKey.SizeInBytes} bytes.", nameof(keyMaterial));
        }

        KeyMaterial = keyMaterial;
        ImportResult = importResult;
    }

    public byte[] KeyMaterial { get; private set; }
    public RecoveryKitImportResult ImportResult { get; }

    public void Dispose()
    {
        if (KeyMaterial.Length == 0)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(KeyMaterial);
        KeyMaterial = [];
    }
}
