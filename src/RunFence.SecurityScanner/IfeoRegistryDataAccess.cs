using System.Security.AccessControl;
using Microsoft.Win32;

namespace RunFence.SecurityScanner;

/// <summary>
/// Implements registry data access for Image File Execution Options (IFEO).
/// </summary>
public class IfeoRegistryDataAccess : IIfeoRegistryAccess
{
    public RegistrySecurity? GetIfeoRegistryKeySecurity()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
                RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
            return key?.GetAccessControl();
        }
        catch
        {
            return null;
        }
    }

    public RegistrySecurity? GetIfeoWow6432RegistryKeySecurity()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Wow6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
                RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
            return key?.GetAccessControl();
        }
        catch
        {
            return null;
        }
    }

    public List<string> GetIfeoSubkeyNames()
    {
        var names = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options");
            if (key != null)
                names.AddRange(key.GetSubKeyNames());
        }
        catch
        {
            /* not accessible */
        }

        return names;
    }

    public string? GetIfeoDebuggerPath(string exeName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{exeName}");
            var debugger = key?.GetValue("Debugger") as string;
            if (string.IsNullOrWhiteSpace(debugger))
                return null;
            return CommandLineParser.ExtractExecutablePath(debugger);
        }
        catch
        {
            return null;
        }
    }

    public string? GetIfeoVerifierDlls(string exeName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{exeName}");
            return key?.GetValue("VerifierDlls") as string;
        }
        catch
        {
            return null;
        }
    }
}
