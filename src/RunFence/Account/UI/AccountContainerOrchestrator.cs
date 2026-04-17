using RunFence.Account.Lifecycle;
using RunFence.Account.UI.AppContainer;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Licensing;

namespace RunFence.Account.UI;

public class AccountContainerOrchestrator(
    IModalCoordinator modalCoordinator,
    SessionPersistenceHelper persistenceHelper,
    IContainerDeletionService containerDeletion,
    AppContainerEditService containerEditService,
    AccountAclManagerLauncher aclManagerLauncher,
    ILicenseService licenseService,
    ContainerDeletionCleanupHelper cleanupHelper,
    ShellHelper shellHelper,
    ISessionProvider sessionProvider)
{
    public static readonly string[] InternetCapabilitySids = ["S-1-15-3-1", "S-1-15-3-2"];

    public void CreateContainer(IWin32Window? parent, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        var db = session.Database;
        if (!licenseService.CanCreateContainer(db.AppContainers.Count))
        {
            MessageBox.Show(licenseService.GetRestrictionMessage(EvaluationFeature.Containers, db.AppContainers.Count),
                "License Limit", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var isFirst = db.AppContainers.Count == 0;
        using var dlg = new AppContainerEditDialog(null, containerEditService);
        if (modalCoordinator.ShowModal(dlg, parent) != DialogResult.OK)
            return;
        persistenceHelper.SaveConfig(db, session.PinDerivedKey, session.CredentialStore.ArgonSalt);
        onSaved();
        if (isFirst)
            MessageBox.Show(
                "AppContainers provide isolation for the apps running inside them, but keep in mind:\n\n" +
                "\u2022 The interactive account and Administrators can freely read and modify the container\u2019s data folder.\n" +
                "\u2022 The interactive account can freely read the container\u2019s app RAM.\n" +
                "\u2022 The container can read the registry, Program Files, and system directories by default.\n\n" +
                "Do not rely on AppContainer isolation as a security boundary against the local administrator or interactive account.",
                "AppContainer Security Reminder", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public void EditContainer(ContainerRow row, IWin32Window? parent, Action onSaved)
    {
        using var dlg = new AppContainerEditDialog(row.Container, containerEditService);
        if (modalCoordinator.ShowModal(dlg, parent) != DialogResult.OK)
        {
            if (dlg.DeleteRequested)
                DeleteContainer(row, onSaved);
            return;
        }

        var session = sessionProvider.GetSession();
        persistenceHelper.SaveConfig(session.Database, session.PinDerivedKey, session.CredentialStore.ArgonSalt);
        onSaved();
    }

    public void DeleteContainer(ContainerRow row, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        var db = session.Database;
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

        var allAppsSnapshot = db.Apps.ToList();
        var remainingApps = db.Apps.Where(a => !containerApps.Contains(a)).ToList();

        if (!containerDeletion.DeleteContainer(container, row.ContainerSid))
        {
            MessageBox.Show("Delete failed: unable to remove AppContainer profile.", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        cleanupHelper.CleanupContainerApps(containerApps, allAppsSnapshot, remainingApps);
        persistenceHelper.SaveConfig(db, session.PinDerivedKey, session.CredentialStore.ArgonSalt);
        onSaved();
    }

    public void ToggleContainerInternet(ContainerRow row, bool enable, Action onSaved)
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
        if (!changed)
            return;

        container.Capabilities = caps.Count > 0 ? caps : null;
        var session = sessionProvider.GetSession();
        persistenceHelper.SaveConfig(session.Database, session.PinDerivedKey, session.CredentialStore.ArgonSalt);
        onSaved();
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

    public void OpenContainerProfileFolder(ContainerRow row)
    {
        var path = AppContainerPaths.GetContainerDataPath(row.Container.Name);
        try
        {
            shellHelper.OpenInExplorer(path);
        }
        catch
        {
            /* best effort */
        }
    }

    public void OpenAclManager(ContainerRow row, IWin32Window? parent)
    {
        aclManagerLauncher.OpenAclManager(row, parent);
    }

    public void OpenAclManager(AccountRow row, IWin32Window? parent)
    {
        aclManagerLauncher.OpenAclManager(row, parent);
    }
}