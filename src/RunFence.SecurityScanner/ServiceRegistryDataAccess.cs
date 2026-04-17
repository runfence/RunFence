using System.Security.AccessControl;
using Microsoft.Win32;

namespace RunFence.SecurityScanner;

/// <summary>
/// Implements registry data access for Windows services.
/// </summary>
public class ServiceRegistryDataAccess(Action<string> logError) : IServiceRegistryAccess
{
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
            logError($"Failed to enumerate services: {ex.Message}");
        }

        return services;
    }
}
