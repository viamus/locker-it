namespace Lockerit.Core.Security;

public sealed record RecoveryKitMetadata(
    string FilePath,
    DateTimeOffset CreatedAtUtc,
    string? PassphraseHint,
    string KdfName,
    int KdfIterations);
