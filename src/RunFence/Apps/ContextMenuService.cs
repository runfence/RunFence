using Microsoft.Win32;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Apps;

public class ContextMenuService(
    ILoggingService log,
    IAppIconProvider iconProvider,
    IProgramDataObjectProvisioner programDataObjectProvisioner,
    IProgramDataKnownPathResolver programDataKnownPathResolver) : IContextMenuService
{
    private readonly IRegistryKey _hklm = new WindowsRegistryKey(Registry.LocalMachine);
    private readonly string? _launcherPathOverride;

    public ContextMenuService(
        ILoggingService log,
        IAppIconProvider iconProvider,
        IProgramDataObjectProvisioner programDataObjectProvisioner,
        IProgramDataKnownPathResolver programDataKnownPathResolver,
        IRegistryKey hklm,
        string? launcherPathOverride)
        : this(log, iconProvider, programDataObjectProvisioner, programDataKnownPathResolver)
    {
        _hklm = hklm;
        _launcherPathOverride = launcherPathOverride;
    }

    public void Register()
    {
        log.Info("ContextMenuService: registering context menu.");
        var launcherPath = _launcherPathOverride ?? Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName);
        if (!File.Exists(launcherPath))
        {
            log.Warn($"Launcher not found at {launcherPath}, skipping context menu registration");
            return;
        }

        ExportIcon();

        foreach (var registryPath in PathConstants.ContextMenuRegistryPaths)
        {
            try
            {
                using var shellKey = _hklm.CreateSubKey(registryPath);
                shellKey.SetValue(null, "RunFence...");
                shellKey.SetValue("Icon", programDataKnownPathResolver.GetFilePath(ProgramDataPolicies.ContextMenuIcon));

                using var commandKey = shellKey.CreateSubKey("command");
                commandKey.SetValue(null, $"\"{launcherPath}\" \"%1\"");
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to register context menu for '{registryPath}': {ex.Message}");
            }
        }

        log.Info("Context menu registered");
    }

    public void Unregister()
    {
        log.Info("ContextMenuService: unregistering context menu.");
        foreach (var registryPath in PathConstants.ContextMenuRegistryPaths)
        {
            try
            {
                _hklm.DeleteSubKeyTree(registryPath, false);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to remove context menu registry key '{registryPath}': {ex.Message}");
            }
        }

        try
        {
            var iconPath = programDataKnownPathResolver.GetFilePath(ProgramDataPolicies.ContextMenuIcon);
            if (File.Exists(iconPath))
                File.Delete(iconPath);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to delete exported icon: {ex.Message}");
        }

        log.Info("Context menu unregistered");
    }

    private void ExportIcon()
    {
        try
        {
            var icon = iconProvider.GetAppIcon();
            using (var fs = programDataObjectProvisioner.CreateOrReplaceFile(
                       ProgramDataPolicies.ContextMenuIcon,
                       FileShare.Read))
            {
                icon.Save(fs);
                fs.Flush();
            }

        }
        catch (Exception ex)
        {
            log.Warn($"Failed to export icon: {ex.Message}");
        }
    }
}
