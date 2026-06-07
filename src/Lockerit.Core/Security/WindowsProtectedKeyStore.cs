using System.Security.Cryptography;
using System.Text;
using Lockerit.Core.Storage;

namespace Lockerit.Core.Security;

public sealed class WindowsProtectedKeyStore
{
    private const string KeyHeader = "LockeritKey.v1.";
    private readonly LockeritPaths _paths;

    public WindowsProtectedKeyStore(LockeritPaths paths)
    {
        _paths = paths;
    }

    public bool KeyFileExists => File.Exists(_paths.KeyFilePath);

    public KeyOpenResult OpenOrCreate()
    {
        Directory.CreateDirectory(_paths.RootDirectory);

        if (File.Exists(_paths.KeyFilePath))
        {
            return new KeyOpenResult(new VaultKey(ReadProtectedKey()), CreatedNewKey: false);
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

    public void SaveImportedKey(ReadOnlySpan<byte> rawKey)
    {
        if (rawKey.Length != VaultKey.SizeInBytes)
        {
            throw new ArgumentException($"The imported Lockerit vault key must be {VaultKey.SizeInBytes} bytes.", nameof(rawKey));
        }

        Directory.CreateDirectory(_paths.RootDirectory);
        WriteProtectedKey(rawKey);
    }

    private void WriteProtectedKey(ReadOnlySpan<byte> rawKey)
    {
        var keyCopy = rawKey.ToArray();
        byte[]? protectedKey = null;

        try
        {
            protectedKey = ProtectedData.Protect(keyCopy, GetAdditionalEntropy(), DataProtectionScope.CurrentUser);
            var encodedKey = KeyHeader + Convert.ToBase64String(protectedKey);
            var temporaryPath = _paths.KeyFilePath + ".tmp";

            File.WriteAllText(temporaryPath, encodedKey, Encoding.UTF8);
            File.Move(temporaryPath, _paths.KeyFilePath, overwrite: true);
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

    private byte[] ReadProtectedKey()
    {
        var encodedKey = File.ReadAllText(_paths.KeyFilePath, Encoding.UTF8).Trim();
        if (!encodedKey.StartsWith(KeyHeader, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Lockerit key file format is not supported.");
        }

        var protectedKey = Convert.FromBase64String(encodedKey[KeyHeader.Length..]);
        var rawKey = ProtectedData.Unprotect(protectedKey, GetAdditionalEntropy(), DataProtectionScope.CurrentUser);

        if (rawKey.Length != VaultKey.SizeInBytes)
        {
            CryptographicOperations.ZeroMemory(rawKey);
            throw new InvalidOperationException("The Lockerit key file is invalid.");
        }

        return rawKey;
    }

    private static byte[] GetAdditionalEntropy()
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes("Lockerit.LocalVault.MasterKey.v1"));
    }
}
