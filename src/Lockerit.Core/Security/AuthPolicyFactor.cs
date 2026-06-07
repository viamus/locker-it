namespace Lockerit.Core.Security;

public enum AuthPolicyFactor
{
    WindowsUser = 0,
    MasterPassword = 1,
    Totp = 2
}
