using Lockerit.Core;
using Lockerit.Core.Models;
using Lockerit.Core.Security;
using Lockerit.Core.Storage;
using System.Security.Cryptography;

var root = Path.Combine(Path.GetTempPath(), "Lockerit.SmokeTests", Guid.NewGuid().ToString("N"));
var paths = LockeritPaths.ForRoot(root);
var recoveryKitPath = Path.Combine(root, "lockerit-recovery.lockerit-recovery.json");
var recoveryPassphrase = "Correct horse battery 2026!";
var masterPassword = "Local master password 2026!";
Guid savedSecretId;
Guid savedFileId;

try
{
    using (var vault = LockeritVault.UnlockWithCurrentWindowsUser(paths))
    {
        var secret = PasswordSecret.Create(
            "Email pessoal",
            "Personal",
            "user@example.com",
            "UltraSecretPassword!42",
            "https://example.com",
            "Conta de smoke test");

        vault.SavePassword(secret);

        var saved = vault.ListPasswords().Single();
        Require(saved.Title == secret.Title, "Saved title mismatch.");
        Require(saved.Category == secret.Category, "Saved category mismatch.");
        Require(saved.UserName == secret.UserName, "Saved user name mismatch.");
        Require(saved.Password.Length == 0, "Password list summaries must not keep decrypted passwords.");
        Require(saved.Notes.Length == 0, "Password list summaries must not keep decrypted notes.");
        Require(saved.Url == secret.Url, "Saved URL mismatch.");

        var fullSecret = vault.GetPassword(saved.Id);
        Require(fullSecret.Password == secret.Password, "Saved password mismatch.");
        Require(fullSecret.Notes == secret.Notes, "Saved notes mismatch.");

        var fileBytes = "Identity scan bytes should stay encrypted."u8.ToArray();
        var attachment = VaultFileAttachment.Create(
            "identity-scan.txt",
            "Personal",
            "Smoke test file",
            "text/plain",
            fileBytes);
        vault.SaveFileAttachment(attachment);
        savedFileId = attachment.Id;

        var fileSummary = vault.ListFileAttachments().Single();
        Require(fileSummary.Content.Length == 0, "File list summaries must not keep decrypted file bytes.");
        Require(fileSummary.Notes.Length == 0, "File list summaries must not keep decrypted file notes.");
        Require(fileSummary.Size == fileBytes.Length, "File summary size mismatch.");

        var fullAttachment = vault.GetFileAttachment(savedFileId);
        Require(fullAttachment.Content.AsSpan().SequenceEqual(fileBytes), "Recovered file content mismatch.");

        var plaintextPassword = "UltraSecretPassword!42"u8.ToArray();
        Require(!FileContains(paths.DatabasePath, plaintextPassword), "Plain password leaked into database bytes.");
        Require(!FileContains(paths.DatabasePath + "-wal", plaintextPassword), "Plain password leaked into SQLite WAL bytes.");
        Require(!FileContains(paths.DatabasePath, fileBytes), "Plain file leaked into database bytes.");
        Require(!FileContains(paths.DatabasePath + "-wal", fileBytes), "Plain file leaked into SQLite WAL bytes.");

        vault.ExportRecoveryKit(recoveryKitPath, recoveryPassphrase, "horse phrase");
        vault.ReprotectWindowsKeyringForCurrentUser();
        savedSecretId = secret.Id;

        Require(File.Exists(recoveryKitPath), "Recovery Kit was not exported.");
        Require(!FileContains(recoveryKitPath, plaintextPassword), "Plain password leaked into Recovery Kit bytes.");

        var metadata = LockeritVault.ReadRecoveryKitMetadata(recoveryKitPath);
        Require(metadata.PassphraseHint == "horse phrase", "Recovery Kit hint mismatch.");
    }

    using (var reopenedVault = LockeritVault.UnlockWithCurrentWindowsUser(paths))
    {
        Require(!reopenedVault.CreatedNewKey, "Reopening the same vault should reuse the protected key.");
        Require(reopenedVault.ListPasswords().Single().Id == savedSecretId, "Reopened vault should contain the saved secret.");
        Require(reopenedVault.ListFileAttachments().Single().Id == savedFileId, "Reopened vault should contain the saved file.");
    }

    File.Delete(paths.KeyFilePath);

    RequireThrows<InvalidOperationException>(
        () => LockeritVault.UnlockWithCurrentWindowsUser(paths).Dispose(),
        "Unlocking an existing vault without a keyring should require Recovery Kit import.");

    RequireThrows<CryptographicException>(
        () => LockeritVault.ImportRecoveryKitForCurrentWindowsUser(paths, recoveryKitPath, "wrong passphrase"),
        "Importing with a wrong recovery passphrase should fail.");

    var importResult = LockeritVault.ImportRecoveryKitForCurrentWindowsUser(paths, recoveryKitPath, recoveryPassphrase);
    Require(File.Exists(paths.KeyFilePath), "Recovery import did not recreate the local keyring.");
    Require(importResult.FilePath == recoveryKitPath, "Recovery import returned an unexpected kit path.");

    using (var recoveredVault = LockeritVault.UnlockWithCurrentWindowsUser(paths))
    {
        Require(!recoveredVault.CreatedNewKey, "Recovered vault should reuse the imported keyring.");
        var recovered = recoveredVault.ListPasswords().Single();
        Require(recovered.Id == savedSecretId, "Recovered vault did not open the original secret.");
        Require(recovered.Password.Length == 0, "Recovered list summary should not include password.");
        Require(recoveredVault.GetPassword(savedSecretId).Password == "UltraSecretPassword!42", "Recovered secret password mismatch.");
        Require(recoveredVault.GetFileAttachment(savedFileId).Content.Length > 0, "Recovered file missing content.");

        recoveredVault.EnableMasterPasswordForCurrentUser(masterPassword);
    }

    RequireThrows<VaultMasterPasswordRequiredException>(
        () => LockeritVault.UnlockWithCurrentWindowsUser(paths).Dispose(),
        "Master password protected vault should require a master password.");

    RequireThrows<CryptographicException>(
        () => LockeritVault.UnlockWithCurrentWindowsUser(paths, "wrong master password").Dispose(),
        "Wrong master password should fail.");

    using (var masterVault = LockeritVault.UnlockWithCurrentWindowsUser(paths, masterPassword))
    {
        Require(masterVault.KeyProtectionMode == KeyProtectionMode.WindowsUserWithMasterPassword, "Master password mode not detected.");
        masterVault.DisableMasterPasswordForCurrentUser();
        masterVault.DeletePassword(savedSecretId);
        masterVault.DeleteFileAttachment(savedFileId);
        Require(masterVault.ListPasswords().Count == 0, "Deleted password still appears in recovered vault.");
        Require(masterVault.ListFileAttachments().Count == 0, "Deleted file still appears in recovered vault.");
    }

    Console.WriteLine("Lockerit smoke test passed.");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void RequireThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static bool FileContains(string path, ReadOnlySpan<byte> needle)
{
    if (!File.Exists(path))
    {
        return false;
    }

    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    var haystack = memory.ToArray();
    return haystack.AsSpan().IndexOf(needle) >= 0;
}
