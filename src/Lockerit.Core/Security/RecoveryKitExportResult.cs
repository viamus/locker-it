namespace Lockerit.Core.Security;

public sealed record RecoveryKitExportResult(
    string FilePath,
    DateTimeOffset CreatedAtUtc,
    string KeyFingerprint);
