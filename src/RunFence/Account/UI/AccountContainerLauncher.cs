using System.Security.AccessControl;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;

namespace RunFence.Account.UI;

/// <summary>
/// Handles launching tools (CMD, FolderBrowser) inside an AppContainer,
/// including the access grant needed before launch.
/// </summary>
public class AccountContainerLauncher
{
    private readonly IAppContainerService _appContainerService;
    private readonly IPermissionGrantService _permissionGrantService;
    private readonly IAccountCredentialManager _credentialManager;
    private readonly IProfileRepairHelper _profileRepair;
    private readonly ILoggingService _log;

    public AccountContainerLauncher(
        IAppContainerService appContainerService,
        IPermissionGrantService permissionGrantService,
        IAccountCredentialManager credentialManager,
        IProfileRepairHelper profileRepair,
        ILoggingService log)
    {
        _appContainerService = appContainerService;
        _permissionGrantService = permissionGrantService;
        _credentialManager = credentialManager;
        _profileRepair = profileRepair;
        _log = log;
    }

    public void LaunchFolderBrowser(ContainerRow row, AppSettings settings, AppDatabase db,
        CredentialStore store, ProtectedBuffer key)
    {
        var folderBrowserExe = PathHelper.ResolveExePath(settings.FolderBrowserExePath);
        if (string.IsNullOrEmpty(folderBrowserExe) || !File.Exists(folderBrowserExe))
        {
            MessageBox.Show("Folder Browser executable not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var container = row.Container;
        var acPath = AppContainerPaths.GetContainerDataPath(container.Name);

        var tempApp = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "Folder Browser",
            ExePath = folderBrowserExe,
            AppContainerName = container.Name,
            LaunchAsLowIntegrity = true,
            AllowPassingArguments = true,
        };

        try
        {
            EnsureContainerLaunchAccess(folderBrowserExe, row, db, store, key);
            _profileRepair.ExecuteWithProfileRepair(
                () => _appContainerService.Launch(tempApp, container, CommandLineHelper.JoinArgs([acPath])), null);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to launch Folder Browser in container '{container.Name}'", ex);
            MessageBox.Show($"Launch failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void LaunchCmd(ContainerRow row, AppDatabase db, CredentialStore store, ProtectedBuffer key)
    {
        var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        if (!File.Exists(cmdExe))
        {
            MessageBox.Show("cmd.exe not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var container = row.Container;
        var tempApp = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "CMD",
            ExePath = cmdExe,
            AppContainerName = container.Name,
            LaunchAsLowIntegrity = true,
        };

        try
        {
            EnsureContainerLaunchAccess(cmdExe, row, db, store, key);
            _profileRepair.ExecuteWithProfileRepair(
                () => _appContainerService.Launch(tempApp, container, null), null);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to launch CMD in container '{container.Name}'", ex);
            MessageBox.Show($"Launch failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void EnsureContainerLaunchAccess(string exePath, ContainerRow row, AppDatabase db,
        CredentialStore store, ProtectedBuffer key)
    {
        if (string.IsNullOrEmpty(row.ContainerSid))
            return;

        var exeDir = Directory.Exists(exePath) ? exePath : Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(exeDir))
            return;

        // PermissionGrantService handles: ACE grant + AddGrant tracking + traverse + container auto-grant for interactive user
        if (_permissionGrantService.EnsureAccess(exeDir, row.ContainerSid,
                FileSystemRights.ReadAndExecute, confirm: null).DatabaseModified)
            _credentialManager.SaveConfig(db, key, store.ArgonSalt);
    }
}