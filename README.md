# Lockerit

Lockerit is a Windows-first, standalone local vault built with .NET 10 and WPF.

## MVP scope

- Windows Hello/PIN/biometric unlock with a local password fallback for the current Windows account.
- Local SQLite storage.
- Password create, read, update and delete.
- Local password generation.
- Clipboard auto-clear after 30 seconds when the copied value is unchanged.
- Dark desktop UI with 2D vector iconography.
- Sidebar navigation for Passwords and Settings, language preference, and tray behavior.
- Configurable vault database path.
- No WebAPI and no network dependency at runtime.

## Security model

The vault uses a random 256-bit master key generated on first unlock. Daily local unlock is tied to the current Windows account and asks Windows Hello/PIN/biometric consent first, with a Lockerit password dialog fallback for the same Windows account. The master key is stored at:

```text
%APPDATA%\Lockerit\keyring.bin
```

That key file is protected with Windows DPAPI using `DataProtectionScope.CurrentUser`, so the key can only be unprotected by the same Windows user profile. Password records are serialized as JSON and encrypted with AES-256-GCM before being written to SQLite. The database only stores the item ID, item kind and encrypted payload.

The SQLite database is stored at:

```text
%APPDATA%\Lockerit\lockerit.db
```

The Settings tab can point Lockerit to a different `.db` file. For custom paths, the DPAPI-protected key is stored next to the selected vault database using the `<database-name>.keyring.bin` naming pattern.

## Storage choice

Lockerit still uses SQLite for this stage. The security boundary is not SQLite itself; it is the AES-256-GCM encryption performed before each item reaches storage. SQLite gives the app reliable local indexing, atomic updates and mature file handling without inventing a custom password filesystem too early.

A custom encrypted bundle can still be added later if file attachment workflows need it. The current model keeps that door open because the vault stores typed encrypted payloads rather than plain records.

## Desktop experience

- The app uses a dark-only, Doxie-inspired workspace shell.
- The palette follows Doxie tokens: Tigerlily primary, warm ink backgrounds, raised panels, muted cream text and compact 6-8px radii.
- Login is a focused unlock surface with no sidebar.
- The vault view has a left navigation rail, command-style search, password table and modal editor.
- Native WPF menu tabs were removed; actions live in the shell, settings, or tray.
- Settings are a sidebar workspace.
- The login screen can change the vault database path before unlock.
- Minimizing hides Lockerit to the Windows tray.
- Closing the window hides to tray by default; this can be changed in Settings.
- The tray menu can show the app, lock and hide it, or exit.
- Recovery import/export is available from the login recovery card and Settings.

## Recovery

Cross-device support does not copy the Windows DPAPI keyring. Lockerit exports a separate Recovery Kit:

1. Keep the vault database encrypted with the existing random vault master key.
2. Derive a recovery wrapping key from a recovery passphrase using PBKDF2-HMAC-SHA256 with a 256-bit random salt, 600,000 iterations and versioned parameters.
3. Encrypt a copy of the vault master key into a small Recovery Kit file using AES-256-GCM.
4. On another PC, the user selects the vault database and Recovery Kit, enters the recovery passphrase, and Lockerit unwraps the vault master key.
5. Lockerit then stores a new DPAPI-protected keyring for the currently logged-in Windows account.

This means the portable recovery artifact is not the day-to-day unlock key. The Windows account boundary remains local to each device, and the recovery passphrase becomes the cross-device secret.

### Recovery phases

1. Export: open Settings, export a Recovery Kit, and set a recovery passphrase. The Recovery Kit contains no plaintext passwords.
2. Move: copy the vault database and the Recovery Kit to the target device. Keep the passphrase separate from both files.
3. Import: on the target device, choose the vault database on the login screen, import the Recovery Kit, and enter the recovery passphrase.
4. Update: Lockerit writes a new DPAPI-protected keyring for the currently logged-in Windows account. The vault then unlocks normally through Windows Hello/PIN/biometric or the current Windows password fallback.
5. Refresh: Settings can re-save the currently unlocked vault master key into the local DPAPI keyring for the current Windows account.

If the vault database exists but the local keyring is missing, Lockerit refuses to generate a new key and asks for Recovery Kit import instead. This protects the user from accidentally replacing the only key that can decrypt an existing vault.

## Supply-chain posture

The repository keeps dependencies intentionally small:

- `Microsoft.Data.Sqlite`
- `System.Security.Cryptography.ProtectedData`

`NuGet.Config` maps package restore explicitly to `nuget.org`, and package lock files are enabled through `Directory.Build.props`.

## Run

```powershell
dotnet restore Lockerit.slnx
dotnet build Lockerit.slnx
dotnet run --project src/Lockerit.App/Lockerit.App.csproj
```

## Smoke test

```powershell
dotnet run --project tests/Lockerit.Core.SmokeTests/Lockerit.Core.SmokeTests.csproj
```

## Current limitations

- DPAPI protects data at rest for the Windows user, but malware already running as the same unlocked user can still ask Windows to decrypt the key.
- The recovery passphrase cannot be recovered by Lockerit if it is forgotten.
- There is no separate master password or hardware-backed key option yet.
- Files are not implemented yet; the core is structured to add encrypted file payloads next.
- UI memory can still hold decrypted strings while the app is open.
