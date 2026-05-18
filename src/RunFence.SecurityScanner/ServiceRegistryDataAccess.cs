using System.Security.AccessControl;
using Microsoft.Win32;

namespace RunFence.SecurityScanner;

/// <summary>
/// Implements registry data access for Windows services.
/// </summary>
public class ServiceRegistryDataAccess(Action<string> logError) : IServiceRegistryAccess
{
    private const int ServiceTypeKernelDriver = 0x1;
    private const int ServiceTypeFileSystemDriver = 0x2;
    private const int ServiceTypeWin32OwnProcess = 0x10;
    private const int ServiceTypeWin32ShareProcess = 0x20;

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

                    if (svcKey?.GetValue("Type") is not int type || !IsAutoStartType(type))
                        continue;

                    var imagePath = svcKey.GetValue("ImagePath") as string;
                    if (string.IsNullOrWhiteSpace(imagePath))
                        continue;

                    var expanded = NormalizeImagePath(imagePath, isDriver: IsDriverType(type));

                    string? serviceDll = null;
                    var parametersPath = $@"SYSTEM\CurrentControlSet\Services\{serviceName}\Parameters";
                    var parametersSecurity = GetSubkeySecurity(parametersPath);
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

                    services.Add(new ServiceInfo(
                        serviceName,
                        imagePath,
                        expanded,
                        serviceDll,
                        GetSubkeySecurity($@"SYSTEM\CurrentControlSet\Services\{serviceName}"),
                        parametersSecurity));
                }
                catch
                {
                    /* skip individual service */
                }
            }
        }
        catch (Exception ex)
        {
            logError($"Failed to enumerate services: {ex.Message}");
        }

        return services;
    }

    private static bool IsAutoStartType(int type)
    {
        const int supportedMask =
            ServiceTypeKernelDriver |
            ServiceTypeFileSystemDriver |
            ServiceTypeWin32OwnProcess |
            ServiceTypeWin32ShareProcess;
        return (type & supportedMask) != 0;
    }

    private static bool IsDriverType(int type) =>
        (type & (ServiceTypeKernelDriver | ServiceTypeFileSystemDriver)) != 0;

    private static string NormalizeImagePath(string imagePath, bool isDriver)
    {
        var extracted = CommandLineParser.ExtractExecutablePath(imagePath) ?? imagePath;
        var expanded = SecurityScanner.ExpandEnvVars(extracted).Trim();
        if (!isDriver)
            return expanded;

        const string systemRootPrefix = @"\SystemRoot\";
        if (expanded.StartsWith(systemRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return Path.Combine(windowsDir, expanded[systemRootPrefix.Length..]);
        }

        if (expanded.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return Path.Combine(windowsDir, expanded);
        }

        if (expanded.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
            return expanded[4..];

        return expanded;
    }

    private static RegistrySecurity? GetSubkeySecurity(string subKeyPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKeyPath,
                RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
            return key?.GetAccessControl();
        }
        catch
        {
            return null;
        }
    }
}
