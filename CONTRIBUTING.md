# Contributing to LockerIt

Thanks for helping shape LockerIt. This project is a security-sensitive local vault, so every contribution should preserve the local-first model and avoid accidental exposure of runtime secrets.

## Ground Rules

- Keep LockerIt standalone unless a change explicitly documents why a network dependency is required.
- Do not store plaintext secrets in files, logs, SQLite rows, test artifacts, screenshots, or CI output.
- Prefer small dependency changes with locked package restore.
- Keep UI copy in English.
- Keep the dark desktop experience consistent.
- Update `.docs/` when behavior, threat boundaries, recovery, or tooling changes.

## Local Setup

```powershell
dotnet restore Lockerit.slnx
dotnet build Lockerit.slnx
dotnet run --project tests/Lockerit.Core.SmokeTests/Lockerit.Core.SmokeTests.csproj
```

## Pull Request Checklist

Before opening a PR:

```powershell
git status --short --branch
git diff --check
dotnet build Lockerit.slnx
dotnet run --project tests/Lockerit.Core.SmokeTests/Lockerit.Core.SmokeTests.csproj
```

Also verify that no local runtime artifact is staged:

```powershell
git diff --cached --name-only
```

Never commit:

- `*.db`, `*.sqlite`, `*-wal`, `*-shm`;
- `keyring.bin` or `*.keyring.bin`;
- `*.lockerit-recovery.json`;
- `settings.json`;
- `.env` files;
- certificates, private keys, or real recovery material.

## Security-Sensitive Changes

For changes touching cryptography, key storage, recovery, Windows account verification, clipboard behavior, or file attachments, include:

- the user-facing security behavior;
- the threat being reduced;
- residual risk that still remains;
- build/test evidence;
- docs updates.

Do not claim that local malware, a fully compromised Windows profile, or a forgotten recovery passphrase can be solved unless the implementation truly provides a separate recovery mechanism and documents its trade-offs.

## Branches

Use short topic branches:

```powershell
git switch -c codex/<topic>
```

Open pull requests against `main`.
