using Microsoft.Win32;

namespace AionDPS.Ui;

/// <summary>
/// "Start automatically with Windows" (App menu) - a per-user HKCU Run-key entry, no admin rights
/// needed (matches this app's own asInvoker philosophy - see Program.cs's own remarks: reading
/// Chat.log needs no elevation, and neither does this). Points at whatever exe is CURRENTLY
/// running, which under Velopack is always the same stable "current" launcher path regardless of
/// which version is installed, so this never goes stale across an update.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AionDpsMeter";

    /// <summary>Whether SOME value is registered under this app's name - not an exact path match,
    /// so a Run-key entry written by an older/different install of this same app still reads as
    /// "on" rather than silently flipping the menu's checkmark off.</summary>
    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
