namespace Lockerit.Core.Security;

public sealed record RecoveryCodeHash(
    string SaltBase64,
    string HashBase64,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UsedAtUtc);
