using Microsoft.Win32;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Apps;

public class ContextMenuService : IContextMenuService
{
    private readonly ILoggingService _log;
    private readonly IAppIconProvider _iconProvider;

    public ContextMenuService(ILoggingService log, IAppIconProvider iconProvider)
    {
        _log = log;
        _iconProvider = iconProvider;
    }

    public void Register()
    {
        _log.Info("ContextMenuService: registering context menu.");
        var launcherPath = Path.Combine(AppContext.BaseDirectory, Constants.LauncherExeName);
        if (!File.Exists(launcherPath))
        {
            _log.Warn($"Launcher not found at {launcherPath}, skipping context menu registration");
            return;
        }

        ExportIcon();

        foreach (var registryPath in Constants.ContextMenuRegistryPaths)
        {
            try
            {
                using var shellKey = Registry.LocalMachine.CreateSubKey(registryPath);
                shellKey.SetValue(null, "RunFence...");
                shellKey.SetValue("Icon", Constants.ExportedIconPath);

                using var commandKey = shellKey.CreateSubKey("command");
                commandKey.SetValue(null, $"\"{launcherPath}\" \"%1\"");
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to register context menu for '{registryPath}': {ex.Message}");
            }
        }

        _log.Info("Context menu registered");
    }

    public void Unregister()
    {
        _log.Info("ContextMenuService: unregistering context menu.");
        foreach (var registryPath in Constants.ContextMenuRegistryPaths)
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(registryPath, false);
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to remove context menu registry key '{registryPath}': {ex.Message}");
            }
        }

        try
        {
            var iconPath = Constants.ExportedIconPath;
            if (File.Exists(iconPath))
                File.Delete(iconPath);
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to delete exported icon: {ex.Message}");
        }

        _log.Info("Context menu unregistered");
    }

    private void ExportIcon()
    {
        try
        {
            var iconPath = Constants.ExportedIconPath;
            var dir = Path.GetDirectoryName(iconPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var icon = _iconProvider.GetAppIcon();
            using var fs = new FileStream(iconPath, FileMode.Create, FileAccess.Write);
            icon.Save(fs);
        }
        catch (Exception ex)
        {
            _log.Warn($"Failed to export icon: {ex.Message}");
        }
    }
}