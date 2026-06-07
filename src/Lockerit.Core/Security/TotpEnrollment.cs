namespace Lockerit.Core.Security;

public sealed record TotpEnrollment(
    string Issuer,
    string AccountName,
    string SecretBase32,
    string SetupUri,
    IReadOnlyList<string> RecoveryCodes);
