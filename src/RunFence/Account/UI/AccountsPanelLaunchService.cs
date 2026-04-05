using System.Security;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Account.UI;

/// <summary>
/// Handles account launch operations (CMD, Folder Browser, Environment Variables, package install)
/// and tray-pin toggles for the accounts panel.
/// Encapsulates <see cref="AccountLauncher"/> and <see cref="AccountLaunchOrchestrator"/> interactions
/// so the panel deals only with high-level launch requests.
/// </summary>
public class AccountsPanelLaunchService(
    AccountLauncher launcher,
    AccountLaunchOrchestrator launchOrchestrator,
    ISessionProvider sessionProvider)
{
    public void OpenCmd(AccountRow accountRow, LaunchFlags flags)
    {
        var session = sessionProvider.GetSession();
        launchOrchestrator.LaunchCmd(accountRow, session.CredentialStore, session.PinDerivedKey, session.Database.SidNames, flags);
    }

    public void OpenFolderBrowser(AccountRow accountRow, LaunchFlags flags, IWin32Window? owner)
    {
        var session = sessionProvider.GetSession();
        launchOrchestrator.LaunchFolderBrowser(accountRow, session.CredentialStore, session.PinDerivedKey,
            session.Database.Settings, owner, session.Database.SidNames, flags, session.Database);
    }

    public void OpenEnvironmentVariables(AccountRow accountRow, LaunchFlags flags)
    {
        var session = sessionProvider.GetSession();
        launchOrchestrator.LaunchEnvironmentVariables(accountRow, session.CredentialStore, session.PinDerivedKey,
            session.Database.SidNames, flags);
    }

    public void InstallPackage(InstallablePackage package, AccountRow accountRow)
    {
        var session = sessionProvider.GetSession();
        launchOrchestrator.InstallPackage(package, accountRow, session.CredentialStore, session.PinDerivedKey, session.Database.SidNames);
    }

    public void InstallPackages(IReadOnlyList<InstallablePackage> packages, AccountRow accountRow)
    {
        var session = sessionProvider.GetSession();
        launchOrchestrator.InstallPackages(packages, accountRow, session.CredentialStore, session.PinDerivedKey, session.Database.SidNames);
    }

    public void InstallPackages(IReadOnlyList<InstallablePackage> packages, CredentialEntry credEntry, SecureString? password)
    {
        var session = sessionProvider.GetSession();
        launchOrchestrator.InstallPackages(packages, credEntry, password, session.Database.SidNames);
    }

    public void ToggleFolderBrowserTray(AccountRow accountRow, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        launchOrchestrator.ToggleFolderBrowserTray(accountRow, session.Database, session.CredentialStore, session.PinDerivedKey, onSaved);
    }

    public void ToggleDiscoveryTray(AccountRow accountRow, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        launchOrchestrator.ToggleDiscoveryTray(accountRow, session.Database, session.CredentialStore, session.PinDerivedKey, onSaved);
    }

    public void ToggleTerminalTray(AccountRow accountRow, Action onSaved)
    {
        var session = sessionProvider.GetSession();
        launchOrchestrator.ToggleTerminalTray(accountRow, session.Database, session.CredentialStore, session.PinDerivedKey, onSaved);
    }

    public bool IsWindowsTerminal(string sid) => launcher.ResolveTerminalExe(sid) != "cmd.exe";

    public bool IsPackageInstalled(InstallablePackage package, string sid) => launcher.IsPackageInstalled(package, sid);
}