using System.Security.AccessControl;
using Microsoft.Win32;

namespace RunFence.SecurityScanner;

/// <summary>
/// Registry data access for system-level autorun locations: Winlogon, AppInit, IFEO,
/// services, print monitors, LSA packages, and network providers.
/// </summary>
public class SystemRegistryDataAccess(Action<string>? logError = null)
{
    private readonly Action<string> _logError = logError ?? Console.Error.WriteLine;

    public RegistrySecurity? GetServiceRegistryKeySecurity(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}",
                RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
            return key?.GetAccessControl();
        }
        catch
        {
            return null;
        }
    }

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

    public List<ServiceInfo> GetAutoStartServices()
    {
        var services = new List<ServiceInfo>();
        try
        {
            using var servicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (servicesKey == null)
                return services;

            foreach (var serviceName in servicesKey.GetSubKeyNames())
            {
                try
                {
                    using var svcKey = servicesKey.OpenSubKey(serviceName);

                    var start = svcKey?.GetValue("Start") as int?;
                    if (start is not (0 or 1 or 2))
                        continue;

                    if (svcKey?.GetValue("Type") is not int type || (type & 0x30) == 0)
                        continue;

                    var imagePath = svcKey.GetValue("ImagePath") as string;
                    if (string.IsNullOrWhiteSpace(imagePath))
                        continue;

                    var expanded = SecurityScanner.ExpandEnvVars(CommandLineParser.ExtractExecutablePath(imagePath) ?? imagePath);

                    string? serviceDll = null;
                    if (expanded.Contains("svchost", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using var parametersKey = svcKey.OpenSubKey("Parameters");
                            serviceDll = parametersKey?.GetValue("ServiceDll") as string;
                            if (serviceDll != null)
                                serviceDll = SecurityScanner.ExpandEnvVars(serviceDll);
                        }
                        catch
                        {
                            /* no parameters key */
                        }
                    }

                    services.Add(new ServiceInfo(serviceName, imagePath, expanded, serviceDll));
                }
                catch
                {
                    /* skip individual service */
                }
            }
        }
        catch (Exception ex)
        {
            _logError($"Failed to enumerate services: {ex.Message}");
        }

        return services;
    }

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