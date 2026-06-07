using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Lockerit.Core.Security;
using Microsoft.Win32.SafeHandles;
using Windows.Security.Credentials.UI;

namespace Lockerit.App.Security;

internal static class WindowsCredentialVerifier
{
    private const int Logon32LogonNetwork = 3;
    private const int Logon32ProviderDefault = 0;

    public static async Task<WindowsCredentialVerificationResult> VerifyCurrentAccountAsync(Window owner, WindowsAccountInfo account)
    {
        var helloResult = await TryVerifyWithWindowsHelloAsync(owner, account);
        if (helloResult.Verified || helloResult.Cancelled || !helloResult.ShouldFallback)
        {
            return helloResult;
        }

        return VerifyWithPassword(owner, account);
    }

    private static async Task<WindowsCredentialVerificationResult> TryVerifyWithWindowsHelloAsync(Window owner, WindowsAccountInfo account)
    {
        try
        {
            var hwnd = new WindowInteropHelper(owner).Handle;
            var message = $"Unlock Lockerit for {account.DisplayName}";
            var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(hwnd, message);

            return result switch
            {
                UserConsentVerificationResult.Verified => WindowsCredentialVerificationResult.Success(),
                UserConsentVerificationResult.Canceled => WindowsCredentialVerificationResult.Cancel(),
                UserConsentVerificationResult.DeviceBusy => WindowsCredentialVerificationResult.Fallback("Windows Hello is busy."),
                UserConsentVerificationResult.DeviceNotPresent => WindowsCredentialVerificationResult.Fallback("Windows Hello is not available on this device."),
                UserConsentVerificationResult.DisabledByPolicy => WindowsCredentialVerificationResult.Fallback("Windows Hello is disabled by policy."),
                UserConsentVerificationResult.NotConfiguredForUser => WindowsCredentialVerificationResult.Fallback("Windows Hello is not configured for this Windows account."),
                UserConsentVerificationResult.RetriesExhausted => WindowsCredentialVerificationResult.Fail("Windows Hello rejected too many attempts."),
                _ => WindowsCredentialVerificationResult.Fallback("Windows Hello is unavailable.")
            };
        }
        catch (Exception ex) when (ex is COMException or TypeLoadException or MissingMethodException)
        {
            return WindowsCredentialVerificationResult.Fallback($"Windows Hello authorization is unavailable. {ex.Message}");
        }
    }

    private static WindowsCredentialVerificationResult VerifyWithPassword(Window owner, WindowsAccountInfo account)
    {
        var dialog = new WindowsPasswordDialog(account)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() != true)
        {
            dialog.ClearPassword();
            return WindowsCredentialVerificationResult.Cancel();
        }

        try
        {
            if (!LogonUser(
                    account.UserName,
                    account.Domain,
                    dialog.Password,
                    Logon32LogonNetwork,
                    Logon32ProviderDefault,
                    out var token))
            {
                var error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return WindowsCredentialVerificationResult.Fail($"Windows rejected this password. {error}");
            }

            token.Dispose();
            return WindowsCredentialVerificationResult.Success();
        }
        finally
        {
            dialog.ClearPassword();
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LogonUser(
        string lpszUsername,
        string lpszDomain,
        string lpszPassword,
        int dwLogonType,
        int dwLogonProvider,
        out SafeAccessTokenHandle phToken);

}

internal sealed record WindowsCredentialVerificationResult(
    bool Verified,
    bool Cancelled,
    bool ShouldFallback,
    string? ErrorMessage)
{
    public static WindowsCredentialVerificationResult Success()
    {
        return new WindowsCredentialVerificationResult(true, false, false, null);
    }

    public static WindowsCredentialVerificationResult Cancel()
    {
        return new WindowsCredentialVerificationResult(false, true, false, null);
    }

    public static WindowsCredentialVerificationResult Fail(string errorMessage)
    {
        return new WindowsCredentialVerificationResult(false, false, false, errorMessage);
    }

    public static WindowsCredentialVerificationResult Fallback(string errorMessage)
    {
        return new WindowsCredentialVerificationResult(false, false, true, errorMessage);
    }
}
