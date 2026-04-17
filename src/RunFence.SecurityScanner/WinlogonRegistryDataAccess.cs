using System.Security.AccessControl;
using Microsoft.Win32;

namespace RunFence.SecurityScanner;

/// <summary>
/// Implements registry data access for Winlogon configuration and AppInit_DLLs.
/// </summary>
public class WinlogonRegistryDataAccess : IWinlogonRegistryAccess
{
    public RegistrySecurity? GetWinlogonRegistryKeySecurity()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
                RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
            return key?.GetAccessControl();
        }
        catch
        {
            return null;
        }
    }

    public List<string> GetWinlogonExePaths()
    {
        var paths = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
            if (key == null)
                return paths;

            paths.AddRange(from valueName in new[] { "Shell", "Userinit" }
                select key.GetValue(valueName) as string
                into value
                where !string.IsNullOrWhiteSpace(value)
                from part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                select CommandLineParser.ExtractExecutablePath(part)
                into exePath
                where !string.IsNullOrEmpty(exePath)
                select SecurityScanner.ExpandEnvVars(exePath));
        }
        catch
        {
            /* Winlogon key not accessible */
        }

        return paths;
    }

    public List<AppInitDllEntry> GetAppInitDllEntries()
    {
        var entries = new List<AppInitDllEntry>();

        CollectAppInitEntry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows",
            @"HKLM\...\Windows (AppInit_DLLs)", entries);
        CollectAppInitEntry(@"SOFTWARE\Wow6432Node\Microsoft\Windows NT\CurrentVersion\Windows",
            @"HKLM\...\Wow6432Node\Windows (AppInit_DLLs)", entries);

        return entries;
    }

    private void CollectAppInitEntry(string subKeyPath, string displayPath, List<AppInitDllEntry> entries)
    {
        try
        {
            RegistrySecurity? security = null;
            var dllPaths = new List<string>();

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(subKeyPath,
                    RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
                security = key?.GetAccessControl();
            }
            catch
            {
                /* no access to ACL */
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(subKeyPath);
                if (key != null)
                {
                    var value = key.GetValue("AppInit_DLLs") as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        dllPaths.AddRange(value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(dll => SecurityScanner.ExpandEnvVars(dll)).Where(expanded => !string.IsNullOrEmpty(expanded)));
                    }
                }
            }
            catch
            {
                /* key not accessible */
            }

            if (security != null || dllPaths.Count > 0)
                entries.Add(new AppInitDllEntry(security, displayPath, dllPaths));
        }
        catch
        {
            /* skip */
        }
    }
}
