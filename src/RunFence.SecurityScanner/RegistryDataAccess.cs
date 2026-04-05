using System.Security.AccessControl;
using Microsoft.Win32;

namespace RunFence.SecurityScanner;

public class RegistryDataAccess
{
    public RegistrySecurity? GetRegistryKeySecurity(RegistryKey hive, string subKeyPath)
    {
        using var key = hive.OpenSubKey(subKeyPath,
            RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
        return key?.GetAccessControl();
    }

    public List<string> GetRegistryAutorunPaths(RegistryKey hive, string subKeyPath)
    {
        var paths = new List<string>();
        try
        {
            using var key = hive.OpenSubKey(subKeyPath);
            if (key == null)
                return paths;

            paths.AddRange(from valueName in key.GetValueNames()
                select key.GetValue(valueName) as string
                into value
                where !string.IsNullOrWhiteSpace(value)
                select CommandLineParser.ExtractExecutablePath(value)
                into exePath
                where !string.IsNullOrEmpty(exePath)
                select exePath);
        }
        catch
        {
            /* key may not exist or be inaccessible */
        }

        return paths;
    }

    public List<(string SubKeyPath, string DisplayPath)> GetWow6432RunKeyPaths(string? userSid)
    {
        var paths = new List<(string, string)>
        {
            (@"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", @"HKLM\...\Wow6432Node\Run"),
            (@"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce", @"HKLM\...\Wow6432Node\RunOnce")
        };
        if (userSid != null)
        {
            paths.Add(($@"{userSid}\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", $@"HKU\{userSid}\...\Wow6432Node\Run"));
            paths.Add(($@"{userSid}\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce", $@"HKU\{userSid}\...\Wow6432Node\RunOnce"));
        }

        return paths;
    }
}