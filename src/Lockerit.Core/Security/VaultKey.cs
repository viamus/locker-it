using System.Security.Cryptography;

namespace Lockerit.Core.Security;

public sealed class VaultKey : IDisposable
{
    public const int SizeInBytes = 32;

    private byte[]? _keyMaterial;

    internal VaultKey(byte[] keyMaterial)
    {
        if (keyMaterial.Length != SizeInBytes)
        {
            throw new ArgumentException($"The Lockerit vault key must be {SizeInBytes} bytes.", nameof(keyMaterial));
        }

        _keyMaterial = keyMaterial;
    }

    internal byte[] CopyKeyMaterial()
    {
        if (_keyMaterial is null)
        {
            throw new ObjectDisposedException(nameof(VaultKey));
        }

        return (byte[])_keyMaterial.Clone();
    }

    public void Dispose()
    {
        if (_keyMaterial is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_keyMaterial);
        _keyMaterial = null;
    }
}
