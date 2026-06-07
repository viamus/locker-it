namespace Lockerit.Core.Security;

public sealed record KeyOpenResult(VaultKey Key, bool CreatedNewKey);
