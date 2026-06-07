namespace Lockerit.Core.Security;

public sealed record AuthPolicyVerificationResult(
    bool Verified,
    bool UsedRecoveryCode,
    string Message)
{
    public static AuthPolicyVerificationResult Success(bool usedRecoveryCode = false)
    {
        return new AuthPolicyVerificationResult(
            true,
            usedRecoveryCode,
            usedRecoveryCode
                ? "Recovery code accepted. This code has been consumed."
                : "Authenticator code accepted.");
    }

    public static AuthPolicyVerificationResult Fail(string message)
    {
        return new AuthPolicyVerificationResult(false, false, message);
    }
}
