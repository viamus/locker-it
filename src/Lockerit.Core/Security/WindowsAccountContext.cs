using System.Security.Principal;

namespace Lockerit.Core.Security;

public sealed record WindowsAccountInfo(string DisplayName, string UserName, string Domain, string? Sid);

public static class WindowsAccountContext
{
    public static WindowsAccountInfo Current()
    {
        using var identity = WindowsIdentity.GetCurrent();

        var displayName = identity?.Name;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = Environment.UserName;
        }

        var domain = Environment.UserDomainName;
        var userName = Environment.UserName;

        var separatorIndex = displayName.IndexOf('\\');
        if (separatorIndex > 0 && separatorIndex < displayName.Length - 1)
        {
            domain = displayName[..separatorIndex];
            userName = displayName[(separatorIndex + 1)..];
        }

        return new WindowsAccountInfo(displayName, userName, domain, identity?.User?.Value);
    }
}
