using System.Security.AccessControl;
using Microsoft.Win32;

namespace RunFence.SecurityScanner;

/// <summary>
/// Implements registry data access for Windows component extension points:
/// print monitors, LSA packages, and network providers.
/// </summary>
public class WindowsComponentRegistryDataAccess : IWindowsComponentRegistryAccess
{
    public List<RegistryDllEntry> GetPrintMonitorEntries()
    {
        var entries = new List<RegistryDllEntry>();
        try
        {
            using var monitorsKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Monitors");
            if (monitorsKey == null)
                return entries;

            foreach (var monitorName in monitorsKey.GetSubKeyNames())
            {
                try
                {
                    RegistrySecurity? security = null;
                    var dllPaths = new List<string>();
                    var displayPath = $@"HKLM\...\Print\Monitors\{monitorName}";
                    var navTarget = $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Print\Monitors\{monitorName}";

                    try
                    {
                        using var monKey = monitorsKey.OpenSubKey(monitorName,
                            RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
                        security = monKey?.GetAccessControl();
                    }
                    catch
                    {
                        /* no access */
                    }

                    try
                    {
                        using var monKey = monitorsKey.OpenSubKey(monitorName);
                        var driver = monKey?.GetValue("Driver") as string;
                        if (!string.IsNullOrEmpty(driver))
                            dllPaths.Add(SecurityScanner.ExpandEnvVars(driver));
                    }
                    catch
                    {
                        /* skip */
                    }

                    if (security != null || dllPaths.Count > 0)
                        entries.Add(new RegistryDllEntry(displayPath, security, dllPaths, navTarget));
                }
                catch
                {
                    /* skip individual monitor */
                }
            }
        }
        catch
        {
            /* Print Monitors key not accessible */
        }

        return entries;
    }

    public List<(RegistrySecurity? Security, List<string> DllPaths)> GetLsaPackageEntries()
    {
        var entries = new List<(RegistrySecurity?, List<string>)>();
        try
        {
            RegistrySecurity? security = null;
            var dllPaths = new List<string>();

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa",
                    RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
                security = key?.GetAccessControl();
            }
            catch
            {
                /* no access */
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
                if (key != null)
                {
                    foreach (var valueName in new[] { "Authentication Packages", "Notification Packages", "Security Packages" })
                    {
                        var value = key.GetValue(valueName);
                        if (value is string[] multiString)
                        {
                            dllPaths.AddRange(multiString.Where(dll => !string.IsNullOrWhiteSpace(dll)));
                        }
                    }
                }
            }
            catch
            {
                /* skip */
            }

            if (security != null || dllPaths.Count > 0)
                entries.Add((security, dllPaths));
        }
        catch
        {
            /* LSA key not accessible */
        }

        return entries;
    }

    public List<RegistryDllEntry> GetNetworkProviderEntries()
    {
        var entries = new List<RegistryDllEntry>();
        try
        {
            using var orderKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\NetworkProvider\Order");
            var providerOrder = orderKey?.GetValue("ProviderOrder") as string;
            if (string.IsNullOrWhiteSpace(providerOrder))
                return entries;

            foreach (var providerName in providerOrder.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    var subKeyPath = $@"SYSTEM\CurrentControlSet\Services\{providerName}\NetworkProvider";
                    RegistrySecurity? security = null;
                    var dllPaths = new List<string>();
                    var displayPath = $@"HKLM\...\Services\{providerName}\NetworkProvider";
                    var navTarget = $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{providerName}\NetworkProvider";

                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(subKeyPath,
                            RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
                        security = key?.GetAccessControl();
                    }
                    catch
                    {
                        /* no access */
                    }

                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(subKeyPath);
                        var dll = key?.GetValue("ProviderPath") as string;
                        if (!string.IsNullOrEmpty(dll))
                            dllPaths.Add(SecurityScanner.ExpandEnvVars(dll));
                    }
                    catch
                    {
                        /* skip */
                    }

                    if (security != null || dllPaths.Count > 0)
                        entries.Add(new RegistryDllEntry(displayPath, security, dllPaths, navTarget));
                }
                catch
                {
                    /* skip individual provider */
                }
            }
        }
        catch
        {
            /* NetworkProvider key not accessible */
        }

        return entries;
    }
}
