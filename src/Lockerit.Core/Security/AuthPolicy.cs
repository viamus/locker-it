namespace Lockerit.Core.Security;

public sealed record AuthPolicy
{
    public static readonly Guid PolicyId = Guid.Parse("a4f57392-b2e8-42a4-9ec4-a0e642f2a884");

    public int Version { get; init; } = 1;
    public List<AuthPolicyFactor> RequiredFactors { get; init; } = [AuthPolicyFactor.WindowsUser];
    public string? TotpSecretBase32 { get; init; }
    public DateTimeOffset? TotpEnabledAtUtc { get; init; }
    public List<RecoveryCodeHash> RecoveryCodes { get; init; } = [];
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool IsTotpEnabled =>
        RequiredFactors.Contains(AuthPolicyFactor.Totp) &&
        !string.IsNullOrWhiteSpace(TotpSecretBase32);

    public bool RequiresAdditionalFactor => IsTotpEnabled;

    public int ActiveRecoveryCodeCount => RecoveryCodes.Count(code => code.UsedAtUtc is null);

    public static AuthPolicy Default()
    {
        return new AuthPolicy();
    }

    public AuthPolicy WithRequiredFactor(AuthPolicyFactor factor)
    {
        var factors = RequiredFactors.ToList();
        if (!factors.Contains(factor))
        {
            factors.Add(factor);
        }

        return this with
        {
            RequiredFactors = factors,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public AuthPolicy WithoutRequiredFactor(AuthPolicyFactor factor)
    {
        return this with
        {
            RequiredFactors = RequiredFactors.Where(existing => existing != factor).ToList(),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
