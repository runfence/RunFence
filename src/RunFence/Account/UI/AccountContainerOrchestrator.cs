using RunFence.Account.Lifecycle;
using RunFence.Account.UI.AppContainer;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Licensing;

namespace RunFence.Account.UI;

public class AccountContainerOrchestrator
{
    private readonly SessionPersistenceHelper _persistenceHelper;
    private readonly IContainerDeletionService _containerDeletion;
    private readonly AppContainerEditDialogRunner _dialogRunner;
    private readonly AccountAclManagerLauncher _aclManagerLauncher;
    private readonly ContainerDeletionCleanupHelper _cleanupHelper;
    private readonly ISessionProvider _sessionProvider;
    private readonly IAccountMessageBoxService _messageBoxService;

    public static readonly string[] InternetCapabilitySids = ["S-1-15-3-1", "S-1-15-3-2"];

    public AccountContainerOrchestrator(
        SessionPersistenceHelper persistenceHelper,
        IContainerDeletionService containerDeletion,
        AppContainerEditDialogRunner dialogRunner,
        AccountAclManagerLauncher aclManagerLauncher,
        ContainerDeletionCleanupHelper cleanupHelper,
        ISessionProvider sessionProvider,
        IAccountMessageBoxService messageBoxService)
    {
        _persistenceHelper = persistenceHelper;
        _containerDeletion = containerDeletion;
        _dialogRunner = dialogRunner;
        _aclManagerLauncher = aclManagerLauncher;
        _cleanupHelper = cleanupHelper;
        _sessionProvider = sessionProvider;
        _messageBoxService = messageBoxService;
    }

    public void CreateContainer(IWin32Window? parent, Action onSaved)
    {
        var result = _dialogRunner.CreateContainer(parent);
        if (result.DialogResult != DialogResult.OK)
            return;

        if (result.OperationStatus is AppContainerOperationStatus.Succeeded or AppContainerOperationStatus.SaveFailedAfterOs)
            onSaved();

        if (result.ShowFirstContainerWarning)
            _messageBoxService.Show(
                null,
                "AppContainers provide isolation for the apps running inside them, but keep in mind:\n\n" +
                "\u2022 The interactive account and Administrators can freely read and modify the container\u2019s data folder.\n" +
                "\u2022 The interactive account can freely read the container\u2019s app RAM.\n" +
                "\u2022 The container can read the registry, Program Files, and system directories by default.\n\n" +
                "Do not rely on AppContainer isolation as a security boundary against the local administrator or interactive account.",
                "AppContainer Security Reminder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
    }

    public async Task EditContainer(ContainerRow row, IWin32Window? parent, Action onSaved)
    {
        var result = _dialogRunner.EditContainer(row, parent);
        if (result.DialogResult != DialogResult.OK)
        {
            if (result.DeleteRequested)
                await DeleteContainer(row, onSaved);
            return;
        }

        if (result.OperationStatus is AppContainerOperationStatus.Succeeded or AppContainerOperationStatus.SaveFailedAfterOs)
            onSaved();
    }

    public async Task DeleteContainer(ContainerRow row, Action onSaved)
    {
        var session = _sessionProvider.GetSession();
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

        if (_messageBoxService.Show(
                null,
                msg,
                "Confirm Delete Container",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning)
            != DialogResult.Yes)
            return;

        var containerApps = db.Apps
            .Where(a => string.Equals(a.AppContainerName, container.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var allAppsSnapshot = db.Apps.ToList();
        var remainingApps = db.Apps.Where(a => !containerApps.Contains(a)).ToList();

        var deleteResult = await _containerDeletion.DeleteContainer(container, row.ContainerSid);
        if (!deleteResult.Succeeded)
        {
            _messageBoxService.Show(
                null,
                "Delete failed: unable to remove AppContainer profile.",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        _cleanupHelper.CleanupContainerApps(containerApps, allAppsSnapshot, remainingApps);
        _persistenceHelper.SaveConfig(
            db,
            session.PinDerivedKey,
            session.CredentialStore.ArgonSalt);
        onSaved();

        if (deleteResult.Warnings.Count > 0)
        {
            _messageBoxService.Show(
                null,
                $"AppContainer deletion completed with warnings:\n\n{string.Join("\n", deleteResult.Warnings.Select(w => "\u2022 " + w))}",
                "Warnings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
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
        var session = _sessionProvider.GetSession();
        _persistenceHelper.SaveConfig(
            session.Database,
            session.PinDerivedKey,
            session.CredentialStore.ArgonSalt);
        onSaved();
        MessageBox.Show(
            "Capability changes will take effect on next app launch.",
            "Restart Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public void OpenAclManager(ContainerRow row, IWin32Window? parent)
    {
        _aclManagerLauncher.OpenAclManager(row, parent);
    }

    public void OpenAclManager(AccountRow row, IWin32Window? parent)
    {
        _aclManagerLauncher.OpenAclManager(row, parent);
    }

    public void OpenLowIntegrityAclManager(IWin32Window? parent)
    {
        _aclManagerLauncher.OpenLowIntegrityAclManager(parent);
    }

    public void OpenAppContainersAclManager(IWin32Window? parent)
    {
        _aclManagerLauncher.OpenAppContainersAclManager(parent);
    }
}
