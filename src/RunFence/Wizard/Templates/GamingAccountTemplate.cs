using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Account.UI.Forms;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.UI;
using RunFence.Wizard.UI.Forms;
using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard.Templates;

/// <summary>
/// Wizard template for setting up a dedicated gaming account.
/// Supports two modes:
/// <list type="bullet">
///   <item>Create new account — inserts AccountNameStep dynamically after the account picker.</item>
///   <item>Use existing account — if the account has no stored credential, shows
///         <see cref="CredentialEditDialog"/> on the secure desktop before proceeding.</item>
/// </list>
/// In both cases, grants full ownership of game install folders, creates denied-execute app entries
/// for game launchers, and skips items that already exist (without removing previously added ones).
/// </summary>
public class GamingAccountTemplate(
    WizardTemplateExecutor executor,
    WizardAccountSetupHelperFactory setupHelperFactory,
    WizardFolderGrantHelper folderGrantHelper,
    SessionContext session,
    GamingExistingAccountPreparationService existingAccountPreparationService,
    IWindowsAccountQueryService windowsAccountQueryService,
    WizardAccountPickerStepFactory pickerStepFactory,
    WizardCredentialCollector credentialCollector,
    IShortcutDiscoveryService discoveryService,
    IShortcutIconHelper iconHelper)
    : IWizardTemplate
{
    private readonly CommitData _data = new();

    public string DisplayName => "Gaming Account";
    public string Description => "Isolated gaming account with full access to game folders and launcher shortcuts";
    public string IconEmoji => "\U0001F3AE"; // 🎮
    public Action<IWin32Window>? PostWizardAction => null;

    public void Cleanup()
    {
        _data.Password?.Dispose();
        _data.CollectedPassword?.Dispose();
    }

    public IReadOnlyList<WizardStepPage> CreateSteps()
    {
        _data.Reset();

        string? interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        var data = _data;

        var pickerStep = pickerStepFactory.CreatePickerStep(
            setSelection: (sid, isCreate) =>
            {
                data.ExistingAccountSid = sid;
                data.IsExistingAccount = !isCreate;
            },
            options: new AccountPickerStepOptions(
                Credentials: session.CredentialStore.Credentials,
                SidNames: session.Database.SidNames,
                GroupSid: GroupFilterHelper.UsersSid,
                StepTitle: "Gaming Account",
                InfoText: "Select an existing user account to use as the gaming account, or choose " +
                          "\"Create new account\" to create a dedicated one. " +
                          "Accounts with a green dot have stored credentials; gray dot means you will be prompted for a password.",
                InteractiveUserSid: interactiveSid,
                ExcludeAdmins: true,
                DefaultToCreateNew: true),
            followingStepsFactory: isCreate =>
            {
                var instructionsStep = new GamingSetupInstructionsStep(isCreateNew: isCreate);
                var foldersStep = new GamingFoldersStep(paths => data.GameFolders = paths);
                var launchersStep = new GamingLaunchersStep(
                    launchers => data.GameLaunchers = launchers,
                    discoveryService,
                    iconHelper,
                    getSid: () => data.IsExistingAccount ? data.ExistingAccountSid : null);

                if (isCreate)
                {
                    var nameStep = setupHelperFactory.CreateAccountNameStep(
                        (name, password) =>
                        {
                            data.Username = name;
                            data.Password?.Dispose();
                            data.Password = password;
                        },
                        showPassword: true,
                        requirePassword: true,
                        description: "Choose a name and password for the gaming account. " +
                                     "A password is required because the account needs to log in interactively via Win+L " +
                                     "so you can install and update games from their launchers.");
                    return [instructionsStep, nameStep, foldersStep, launchersStep];
                }

                return [instructionsStep, foldersStep, launchersStep];
            },
            commitAction: progress =>
            {
                if (!_data.IsExistingAccount || string.IsNullOrEmpty(_data.ExistingAccountSid))
                    return Task.CompletedTask;
                var pw = credentialCollector.CollectCredentialForStep(_data.ExistingAccountSid, progress);
                if (pw != null) _data.CollectedPassword = pw;
                return Task.CompletedTask;
            });

        return [pickerStep];
    }

    public async Task ExecuteAsync(IWizardProgressReporter progress)
    {
        if (_data.IsExistingAccount)
            await ExecuteForExistingAccountAsync(progress);
        else
            await ExecuteForNewAccountAsync(progress);
    }

    private async Task ExecuteForNewAccountAsync(IWizardProgressReporter progress)
    {
        if (string.IsNullOrEmpty(_data.Username))
        {
            progress.ReportError("No account name was provided.");
            return;
        }

        progress.ReportStatus($"Creating account '{_data.Username}'...");

        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            Username: _data.Username,
            Password: _data.Password?.Copy() ?? ProtectedString.CreateEmpty(),
            ConfirmPassword: _data.Password?.Copy() ?? ProtectedString.CreateEmpty(),
            IsEphemeral: false,
            CheckedGroups: [(GroupFilterHelper.UsersSid, "Users")],
            UncheckedGroups: [],
            AllowLogon: true,
            AllowNetworkLogin: false,
            AllowBgAutorun: false,
            CurrentHiddenCount: 0);

        using var defaults = setupHelperFactory.CreateAccountDefaults();
        var setupOptions = new WizardSetupOptions(
            StoreCredential: true,
            IsEphemeral: false,
            PrivilegeLevel: PrivilegeLevel.Isolated,
            FirewallSettings: new FirewallAccountSettings { AllowLan = false, AllowLocalhost = false },
            DesktopSettingsPath: defaults.DesktopSettingsPath,
            InstallPackages: null,
            TrayTerminal: false);

        var gameFolders = _data.GameFolders;
        var gameLaunchers = _data.GameLaunchers;

        try
        {
            await executor.ExecuteAsync(new WizardStandardFlowParams(
                Request: request,
                SetupOptions: setupOptions,
                BuildOptionsFactory: sid => BuildLauncherOptions(sid, gameLaunchers, session),
                PreEnforcementAction: async (sess, sid) =>
                {
                    var newFolders = FilterNewFolders(gameFolders, sess, sid);
                    if (newFolders.Count == 0)
                        return;
                    await folderGrantHelper.GrantFolderAccessAsync(
                        newFolders, sid,
                        new SavedRightsState(Execute: true, Write: true, Read: true, Special: true, Own: true),
                        progress);
                },
                CreateDesktopShortcut: true), progress);
        }
        finally
        {
            request.Password.Dispose();
            request.ConfirmPassword.Dispose();
        }
    }

    private async Task ExecuteForExistingAccountAsync(IWizardProgressReporter progress)
    {
        if (string.IsNullOrEmpty(_data.ExistingAccountSid))
        {
            progress.ReportError("No account was selected.");
            return;
        }

        var sid = _data.ExistingAccountSid;
        if (!existingAccountPreparationService.Prepare(
                session,
                sid,
                _data.CollectedPassword,
                progress))
            return;

        var gameFolders = _data.GameFolders;
        var gameLaunchers = _data.GameLaunchers;

        await executor.ExecuteAsync(new WizardStandardFlowParams(
            Request: null,
            SetupOptions: null,
            AccountSid: sid,
            BuildOptionsFactory: resolvedSid => BuildLauncherOptions(resolvedSid, gameLaunchers, session),
            PreEnforcementAction: async (sess, resolvedSid) =>
            {
                var newFolders = FilterNewFolders(gameFolders, sess, resolvedSid);
                if (newFolders.Count == 0)
                    return;
                await folderGrantHelper.GrantFolderAccessAsync(
                    newFolders, resolvedSid,
                    new SavedRightsState(Execute: true, Write: true, Read: true, Special: true, Own: true),
                    progress);
            },
            CreateDesktopShortcut: true), progress);
    }

    private IReadOnlyList<AppEntryBuildOptions> BuildLauncherOptions(
        string sid, List<string> launchers, SessionContext sess)
    {
        var existingExePaths = sess.Database.Apps
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
            foreach (var g in account.Grants)
            {
                try
                {
                    existingGrantPaths.Add(Path.GetFullPath(g.Path));
                }
                catch
                {
                }
            }
        }

        return folders
            .Where(f => !string.IsNullOrEmpty(f))
            .Where(f =>
            {
                try
                {
                    return !existingGrantPaths.Contains(Path.GetFullPath(f));
                }
                catch
                {
                    return true;
                }
            })
            .ToList();
    }

    private sealed class CommitData
    {
        public bool IsExistingAccount { get; set; }
        public string? ExistingAccountSid { get; set; }
        public ProtectedString? CollectedPassword { get; set; }
        public string Username { get; set; } = string.Empty;
        public ProtectedString? Password { get; set; }
        public List<string> GameFolders { get; set; } = [];
        public List<string> GameLaunchers { get; set; } = [];

        public void Reset()
        {
            IsExistingAccount = false;
            ExistingAccountSid = null;
            CollectedPassword?.Dispose();
            CollectedPassword = null;
            Username = string.Empty;
            Password?.Dispose();
            Password = null;
            GameFolders = [];
            GameLaunchers = [];
        }
    }
}
