namespace Lockerit.Core.Security;

public sealed class VaultMasterPasswordRequiredException : InvalidOperationException
{
    public VaultMasterPasswordRequiredException()
        : base("This Lockerit vault requires its master password after Windows authorization.")
    {
    }
}
