using Lockerit.Core.Models;
using Lockerit.Core.Security;
using Lockerit.Core.Storage;

namespace Lockerit.Core;

public sealed class LockeritVault : IDisposable
{
    private readonly VaultKey _key;
    private readonly VaultRepository _repository;

    private LockeritVault(
        LockeritPaths paths,
        VaultKey key,
        VaultRepository repository,
        bool createdNewKey,
        WindowsAccountInfo account,
        KeyProtectionMode keyProtectionMode)
    {
        Paths = paths;
        _key = key;
        _repository = repository;
        CreatedNewKey = createdNewKey;
        Account = account;
        KeyProtectionMode = keyProtectionMode;
    }

    public LockeritPaths Paths { get; }
    public bool CreatedNewKey { get; }
    public WindowsAccountInfo Account { get; }
    public KeyProtectionMode KeyProtectionMode { get; private set; }

    public static LockeritVault UnlockWithCurrentWindowsUser(LockeritPaths? paths = null, string? masterPassword = null)
    {
        var resolvedPaths = paths ?? LockeritPaths.ForCurrentUser();
        var keyStore = new WindowsProtectedKeyStore(resolvedPaths);

        if (!keyStore.KeyFileExists && ExistingVaultDatabaseRequiresRecovery(resolvedPaths))
        {
            throw new InvalidOperationException("This vault database already exists, but the Windows keyring for this account is missing. Import a Lockerit Recovery Kit before unlocking this vault on this PC.");
        }

        var keyResult = keyStore.OpenOrCreate(masterPassword);

        try
        {
            var repository = new VaultRepository(resolvedPaths.DatabasePath, keyResult.Key, new AesGcmVaultCipher());
            repository.Initialize();

            return new LockeritVault(
                resolvedPaths,
                keyResult.Key,
                repository,
                keyResult.CreatedNewKey,
                WindowsAccountContext.Current(),
                keyStore.GetProtectionMode());
        }
        catch
        {
            keyResult.Key.Dispose();
            throw;
        }
    }

    public static RecoveryKitImportResult ImportRecoveryKitForCurrentWindowsUser(
        LockeritPaths paths,
        string recoveryKitPath,
        string passphrase)
    {
        if (!File.Exists(paths.DatabasePath))
        {
            throw new InvalidOperationException("Choose the Lockerit vault database before importing a Recovery Kit.");
        }

        var recoveryKit = new RecoveryKitService();
        using var recoveredKey = recoveryKit.Import(recoveryKitPath, passphrase);

        ValidateRecoveredKeyCanOpenVault(paths, recoveredKey.KeyMaterial);

        var keyStore = new WindowsProtectedKeyStore(paths);
        keyStore.SaveImportedKey(recoveredKey.KeyMaterial);

        return recoveredKey.ImportResult;
    }

    public static RecoveryKitMetadata ReadRecoveryKitMetadata(string recoveryKitPath)
    {
        return new RecoveryKitService().ReadMetadata(recoveryKitPath);
    }

    public IReadOnlyList<PasswordSecret> ListPasswords()
    {
        return _repository.GetPasswords()
            .Select(secret => secret.ToSummary())
            .ToArray();
    }

    public PasswordSecret GetPassword(Guid id)
    {
        return _repository.GetPassword(id)
            ?? throw new InvalidOperationException("The Lockerit password entry was not found.");
    }

    public IReadOnlyList<VaultFileAttachment> ListFileAttachments()
    {
        return _repository.GetFileAttachments()
            .Select(file => file.ToSummary())
            .ToArray();
    }

    public VaultFileAttachment GetFileAttachment(Guid id)
    {
        return _repository.GetFileAttachment(id)
            ?? throw new InvalidOperationException("The Lockerit file attachment was not found.");
    }

    public void SavePassword(PasswordSecret secret)
    {
        if (string.IsNullOrWhiteSpace(secret.Title))
        {
            throw new ArgumentException("Password title is required.", nameof(secret));
        }

        if (string.IsNullOrEmpty(secret.Password))
        {
            throw new ArgumentException("Password value is required.", nameof(secret));
        }

        _repository.UpsertPassword(secret);
    }

    public void DeletePassword(Guid id)
    {
        _repository.DeletePassword(id);
    }

    public void SaveFileAttachment(VaultFileAttachment file)
    {
        if (string.IsNullOrWhiteSpace(file.FileName))
        {
            throw new ArgumentException("File name is required.", nameof(file));
        }

        if (file.Content.Length == 0)
        {
            throw new ArgumentException("File content is required.", nameof(file));
        }

        _repository.UpsertFileAttachment(file);
    }

    public void DeleteFileAttachment(Guid id)
    {
        _repository.DeleteFileAttachment(id);
    }

    public RecoveryKitExportResult ExportRecoveryKit(string recoveryKitPath, string passphrase, string? passphraseHint = null)
    {
        var keyMaterial = _key.CopyKeyMaterial();
        try
        {
            return new RecoveryKitService().Export(recoveryKitPath, keyMaterial, passphrase, passphraseHint);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyMaterial);
        }
    }

    public void ReprotectWindowsKeyringForCurrentUser()
    {
        if (KeyProtectionMode == KeyProtectionMode.WindowsUserWithMasterPassword)
        {
            throw new VaultMasterPasswordRequiredException();
        }

        ReprotectWindowsKeyringForCurrentUser(null);
    }

    public void ReprotectWindowsKeyringForCurrentUser(string? masterPassword)
    {
        var keyMaterial = _key.CopyKeyMaterial();
        try
        {
            var keyStore = new WindowsProtectedKeyStore(Paths);
            if (KeyProtectionMode == KeyProtectionMode.WindowsUserWithMasterPassword)
            {
                keyStore.SaveImportedKeyWithMasterPassword(keyMaterial, masterPassword ?? string.Empty);
            }
            else
            {
                keyStore.SaveImportedKey(keyMaterial);
            }
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyMaterial);
        }
    }

    public void EnableMasterPasswordForCurrentUser(string masterPassword)
    {
        var keyMaterial = _key.CopyKeyMaterial();
        try
        {
            new WindowsProtectedKeyStore(Paths).SaveImportedKeyWithMasterPassword(keyMaterial, masterPassword);
            KeyProtectionMode = KeyProtectionMode.WindowsUserWithMasterPassword;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyMaterial);
        }
    }

    public void DisableMasterPasswordForCurrentUser()
    {
        var keyMaterial = _key.CopyKeyMaterial();
        try
        {
            new WindowsProtectedKeyStore(Paths).SaveImportedKey(keyMaterial);
            KeyProtectionMode = KeyProtectionMode.WindowsUser;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyMaterial);
        }
    }

    public void Dispose()
    {
        _key.Dispose();
    }

    private static bool ExistingVaultDatabaseRequiresRecovery(LockeritPaths paths)
    {
        return HasBytes(paths.DatabasePath) || HasBytes(paths.DatabasePath + "-wal");
    }

    private static bool HasBytes(string path)
    {
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private static void ValidateRecoveredKeyCanOpenVault(LockeritPaths paths, byte[] rawKey)
    {
        using var key = new VaultKey((byte[])rawKey.Clone());
        var repository = new VaultRepository(paths.DatabasePath, key, new AesGcmVaultCipher());
        repository.Initialize();
        _ = repository.GetPasswords();
    }
}
