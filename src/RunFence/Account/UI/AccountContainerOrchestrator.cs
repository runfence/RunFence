using RunFence.Account.Lifecycle;
using RunFence.Account.UI.AppContainer;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Licensing;
using RunFence.UI.Forms;

namespace RunFence.Account.UI;

public class AccountContainerOrchestrator
{
    public static readonly string[] InternetCapabilitySids = ["S-1-15-3-1", "S-1-15-3-2"];

    private readonly IAppContainerService _appContainerService;
    private readonly IAccountCredentialManager _credentialManager;
    private readonly ILicenseService _licenseService;
    private readonly IContainerDeletionService _containerDeletion;
    private readonly AccountContainerLauncher _containerLauncher;
    private readonly AccountAclManagerLauncher _aclManagerLauncher;
    private readonly AppContainerEditService _containerEditService;
    private readonly ContainerDeletionCleanupHelper _cleanupHelper;

    public AccountContainerOrchestrator(
        IAppContainerService appContainerService,
        IAccountCredentialManager credentialManager,
        IContainerDeletionService containerDeletion,
        AccountContainerLauncher containerLauncher,
        AppContainerEditService containerEditService,
        AccountAclManagerLauncher aclManagerLauncher,
        ILicenseService licenseService,
        ContainerDeletionCleanupHelper cleanupHelper)
    {
        _appContainerService = appContainerService;
        _credentialManager = credentialManager;
        _licenseService = licenseService;
        _containerDeletion = containerDeletion;
        _containerLauncher = containerLauncher;
        _containerEditService = containerEditService;
        _aclManagerLauncher = aclManagerLauncher;
        _cleanupHelper = cleanupHelper;
    }

    public void CreateContainer(AppDatabase db, CredentialStore store, ProtectedBuffer key, IWin32Window? parent, Action onSaved)
    {
        if (!_licenseService.CanCreateContainer(db.AppContainers.Count))
        {
            MessageBox.Show(_licenseService.GetRestrictionMessage(EvaluationFeature.Containers, db.AppContainers.Count),
                "License Limit", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var isFirst = db.AppContainers.Count == 0;
        using var dlg = new AppContainerEditDialog(null, _appContainerService, _containerEditService);
        if (DataPanel.ShowModal(dlg, parent) != DialogResult.OK)
            return;
        _credentialManager.SaveConfig(db, key, store.ArgonSalt);
        onSaved();
        if (isFirst)
            MessageBox.Show(
                "AppContainers provide isolation for the apps running inside them, but keep in mind:\n\n" +
                "\u2022 The interactive account and Administrators can freely read and modify the container\u2019s data folder.\n" +
                "\u2022 The container can read the registry, Program Files, and system directories by default.\n\n" +
                "Do not rely on AppContainer isolation as a security boundary against the local administrator or interactive account.",
                "AppContainer Security Reminder", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public void EditContainer(ContainerRow row, AppDatabase db, CredentialStore store, ProtectedBuffer key, IWin32Window? parent, Action onSaved)
    {
        using var dlg = new AppContainerEditDialog(row.Container, _appContainerService, _containerEditService);
        if (DataPanel.ShowModal(dlg, parent) != DialogResult.OK)
        {
            if (dlg.DeleteRequested)
                DeleteContainer(row, db, store, key, onSaved);
            return;
        }

        _credentialManager.SaveConfig(db, key, store.ArgonSalt);
        onSaved();
    }

    public void DeleteContainer(ContainerRow row, AppDatabase db, CredentialStore store, ProtectedBuffer key, Action onSaved)
    {
        var container = row.Container;

        var referencingApps = db.Apps
            .Where(a => string.Equals(a.AppContainerName, container.Name, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Name)
            .ToList();

        var msg = $"Delete AppContainer '{container.DisplayName}'?";
        if (referencingApps.Count > 0)
            msg += $"\n\nThe following apps will also be removed:\n{string.Join("\n", referencingApps.Select(n => "\u2022 " + n))}";
        msg += "\n\nThis cannot be undone.";

        if (MessageBox.Show(msg, "Confirm Delete Container", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            != DialogResult.Yes)
            return;

        var containerApps = db.Apps
            .Where(a => string.Equals(a.AppContainerName, container.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var remainingApps = db.Apps.Where(a => !containerApps.Contains(a)).ToList();
        _cleanupHelper.CleanupContainerApps(containerApps, remainingApps);

        if (!_containerDeletion.DeleteContainer(container, row.ContainerSid))
        {
            MessageBox.Show("Delete failed: unable to remove AppContainer profile.", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _credentialManager.SaveConfig(db, key, store.ArgonSalt);
        onSaved();
    }

    public void LaunchFolderBrowser(ContainerRow row, AppSettings settings, AppDatabase db,
        CredentialStore store, ProtectedBuffer key)
    {
        _containerLauncher.LaunchFolderBrowser(row, settings, db, store, key);
    }

    public void ToggleContainerInternet(ContainerRow row, bool enable, AppDatabase db, CredentialStore store, ProtectedBuffer key, Action onSaved)
    {
        var container = row.Container;
        var caps = new List<string>(container.Capabilities ?? []);

        foreach (var sid in InternetCapabilitySids)
        {
            if (enable)
            {
                if (!caps.Contains(sid, StringComparer.OrdinalIgnoreCase))
                    caps.Add(sid);
            }
            else
            {
                caps.RemoveAll(s => string.Equals(s, sid, StringComparison.OrdinalIgnoreCase));
            }
        }

        var oldCaps = container.Capabilities ?? [];
        var changed = !caps.OrderBy(x => x).SequenceEqual(oldCaps.OrderBy(x => x), StringComparer.OrdinalIgnoreCase);
        container.Capabilities = caps.Count > 0 ? caps : null;
        _credentialManager.SaveConfig(db, key, store.ArgonSalt);
        onSaved();
        if (changed)
            MessageBox.Show(
                "Capability changes will take effect on next app launch.",
                "Restart Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public static void CopyContainerProfilePath(ContainerRow row)
    {
        var path = AppContainerPaths.GetContainerDataPath(row.Container.Name);
        try
        {
            Clipboard.SetText(path);
        }
        catch
        {
            /* best effort */
        }
    }

    public static void OpenContainerProfileFolder(ContainerRow row)
    {
        var path = AppContainerPaths.GetContainerDataPath(row.Container.Name);
        try
        {
            ShellHelper.OpenInExplorer(path);
        }
        catch
        {
            /* best effort */
        }
    }

    public void LaunchCmd(ContainerRow row, AppDatabase db, CredentialStore store, ProtectedBuffer key)
    {
        _containerLauncher.LaunchCmd(row, db, store, key);
    }

    public void OpenAclManager(ContainerRow row, IWin32Window? parent)
    {
        _aclManagerLauncher.OpenAclManager(row, parent);
    }

    public void OpenAclManager(AccountRow row, IWin32Window? parent)
    {
        _aclManagerLauncher.OpenAclManager(row, parent);
    }
}