namespace Lockerit.Core.Security;

public sealed record RecoveryKitImportResult(
    string FilePath,
    DateTimeOffset CreatedAtUtc,
    string KeyFingerprint);
