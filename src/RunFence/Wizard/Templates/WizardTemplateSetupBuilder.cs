using System.Diagnostics;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launching.Resolution;
using RunFence.UI;

namespace RunFence.Wizard.Templates;

public class WizardTemplateSetupBuilder(
    WizardAccountSetupHelperFactory setupHelperFactory,
    WizardFolderGrantHelper folderGrantHelper,
    SessionContext session,
    IWindowsAccountQueryService windowsAccountQueryService,
    IExecutableKindService executableKindService)
{
    public WizardStandardFlowParams BuildGamingNewAccountFlow(
        GamingAccountTemplateState state,
        IWizardProgressReporter progress)
    {
        using var defaults = setupHelperFactory.CreateAccountDefaults();

        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            Username: state.Username,
            Password: state.Password?.Copy() ?? ProtectedString.CreateEmpty(),
            ConfirmPassword: state.Password?.Copy() ?? ProtectedString.CreateEmpty(),
            IsEphemeral: false,
            CheckedGroups: [(GroupFilterHelper.UsersSid, "Users")],
            UncheckedGroups: [],
            AllowLogon: true,
            AllowNetworkLogin: false,
            AllowBgAutorun: false,
            CurrentHiddenCount: 0);

        var gameFolders = state.GameFolders.ToList();
        var gameLaunchers = state.GameLaunchers.ToList();
        var setupOptions = new WizardSetupOptions(
            StoreCredential: true,
            IsEphemeral: false,
            PrivilegeLevel: PrivilegeLevel.Isolated,
            FirewallSettings: new FirewallAccountSettings { AllowLan = false, AllowLocalhost = false },
            DesktopSettingsPath: defaults.DesktopSettingsPath,
            InstallPackages: null,
            TrayTerminal: false);

        return new WizardStandardFlowParams(
            Request: request,
            SetupOptions: setupOptions,
            BuildOptionsFactory: sid => BuildGamingLauncherOptions(sid, gameLaunchers),
            PreEnforcementAction: async (sess, sid) =>
            {
                var newFolders = FilterNewFolders(gameFolders, sess, sid);
                if (newFolders.Count == 0)
                    return;

                await folderGrantHelper.GrantFolderAccessAsync(
                    newFolders,
                    sid,
                    new SavedRightsState(Execute: true, Write: true, Read: true, Special: true, Own: true),
                    progress);
            },
            CreateDesktopShortcut: true);
    }

    public WizardStandardFlowParams BuildGamingExistingAccountFlow(
        GamingAccountTemplateState state,
        IWizardProgressReporter progress)
    {
        var gameFolders = state.GameFolders.ToList();
        var gameLaunchers = state.GameLaunchers.ToList();

        return new WizardStandardFlowParams(
            Request: null,
            SetupOptions: null,
            BuildOptionsFactory: sid => BuildGamingLauncherOptions(sid, gameLaunchers),
            AccountSid: state.ExistingAccountSid,
            PreEnforcementAction: async (sess, sid) =>
            {
                var newFolders = FilterNewFolders(gameFolders, sess, sid);
                if (newFolders.Count == 0)
                    return;

                await folderGrantHelper.GrantFolderAccessAsync(
                    newFolders,
                    sid,
                    new SavedRightsState(Execute: true, Write: true, Read: true, Special: true, Own: true),
                    progress);
            },
            CreateDesktopShortcut: true);
    }

    public WizardStandardFlowParams BuildAiAgentFlow(
        AiAgentTemplateState state,
        IWizardProgressReporter progress)
    {
        using var defaults = setupHelperFactory.CreateAccountDefaults();

        var request = EditAccountDialogCreateHandler.CreateAccountRequest.ForIsolatedAccount(
            state.Username,
            defaults.Password);
        var firewallSettings = new FirewallAccountSettings
        {
            AllowInternet = state.AllowInternet,
            AllowLan = state.AllowLan,
            AllowLocalhost = state.AllowLocalhost
        };
        var packages = state.UseAiPackage
            ? new List<InstallablePackage> { KnownPackages.WindowsTerminal, KnownPackages.ClaudeCode }
            : new List<InstallablePackage> { KnownPackages.WindowsTerminal };
        var projectPaths = state.ProjectPaths.ToList();
        var appPath = string.IsNullOrWhiteSpace(state.AppPath)
            ? null
            : state.AppPath;

        return new WizardStandardFlowParams(
            Request: request,
            SetupOptions: new WizardSetupOptions(
                StoreCredential: true,
                IsEphemeral: false,
                PrivilegeLevel: PrivilegeLevel.Isolated,
                FirewallSettings: firewallSettings.IsDefault ? null : firewallSettings,
                DesktopSettingsPath: defaults.DesktopSettingsPath,
                InstallPackages: packages,
                TrayTerminal: true,
                WaitForInstallPackages: true),
            BuildOptionsFactory: sid => BuildAiAgentAppOptions(sid, appPath),
            PreEnforcementAction: async (_, sid) =>
            {
                state.CreatedSid = sid;
                var readWriteSavedRights = new SavedRightsState(
                    Execute: false, Write: true, Read: true, Special: false, Own: false);

                await folderGrantHelper.GrantFolderAccessAsync(
                    projectPaths,
                    sid,
                    readWriteSavedRights,
                    progress);
            },
            CreateDesktopShortcut: !string.IsNullOrEmpty(appPath));
    }

    private IReadOnlyList<AppEntryBuildOptions> BuildGamingLauncherOptions(string sid, List<string> launchers)
    {
        var existingExePaths = session.Database.Apps
            .Where(a => string.Equals(a.AccountSid, sid, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.ExePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string? profilePath = null;
        try
        {
            profilePath = windowsAccountQueryService.GetProfilePath(sid).ProfilePath;
        }
        catch
        {
        }

        return launchers
            .Where(p => !string.IsNullOrEmpty(p) && !existingExePaths.Contains(p))
            .Select(launcherPath =>
            {
                bool inProfile = profilePath != null && IsPathInsideFolder(launcherPath, profilePath);
                return AppEntryBuildOptions.ForWizard(
                    name: Path.GetFileNameWithoutExtension(launcherPath),
                    exePath: launcherPath,
                    accountSid: sid,
                    restrictAcl: !inProfile,
                    aclMode: AclMode.Deny,
                    manageShortcuts: true,
                    aclTarget: AclTarget.Folder);
            })
            .ToList();
    }

    private IReadOnlyList<AppEntryBuildOptions> BuildAiAgentAppOptions(string sid, string? appPath)
    {
        if (string.IsNullOrEmpty(appPath))
            return [];

        string appName;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(appPath);
            appName = !string.IsNullOrWhiteSpace(info.FileDescription)
                ? info.FileDescription
                : Path.GetFileNameWithoutExtension(appPath);
        }
        catch
        {
            appName = Path.GetFileNameWithoutExtension(appPath);
        }

        return
        [
            AppEntryBuildOptions.ForWizard(
                name: appName,
                exePath: appPath,
                accountSid: sid,
                restrictAcl: false,
                aclMode: AclMode.Deny,
                manageShortcuts: true,
                privilegeLevel: executableKindService.IsUwpExeFile(appPath)
                                && session.Database.GetAccount(sid)?.PrivilegeLevel is not (PrivilegeLevel.Basic or PrivilegeLevel.HighIntegrity or PrivilegeLevel.HighestAllowed)
                    ? PrivilegeLevel.Basic
                    : null)
        ];
    }

    private static bool IsPathInsideFolder(string path, string folder)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullFolder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static List<string> FilterNewFolders(List<string> folders, SessionContext sess, string sid)
    {
        var existingGrantPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var account = sess.Database.Accounts.FirstOrDefault(a => string.Equals(a.Sid, sid, StringComparison.OrdinalIgnoreCase));
        if (account != null)
        {
            foreach (var grant in account.Grants)
            {
                try
                {
                    existingGrantPaths.Add(Path.GetFullPath(grant.Path));
                }
                catch
                {
                }
            }
        }

        return folders
            .Where(path => !string.IsNullOrEmpty(path))
            .Where(path =>
            {
                try
                {
                    return !existingGrantPaths.Contains(Path.GetFullPath(path));
                }
                catch
                {
                    return true;
                }
            })
            .ToList();
    }
}
