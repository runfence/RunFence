using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Infrastructure;
using RunFence.Account.UI.Forms;
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
/// <remarks>Deps above threshold: CreateSteps exclusively uses groupMembership, localUserProvider, sidResolver,
/// credentialCollectorFactory, discoveryService; setupHelperFactory is shared with ExecuteForNewAccountAsync.
/// ExecuteForNewAccountAsync and ExecuteForExistingAccountAsync share session, licenseChecker, executor,
/// folderGrantHelper, windowsAccountService (via BuildLauncherOptions) — session is also used in CreateSteps.
/// No split boundary exists without duplicating these shared deps. Reviewed 2026-04-15.</remarks>
public class GamingAccountTemplate(
    WizardTemplateExecutor executor,
    WizardAccountSetupHelperFactory setupHelperFactory,
    WizardFolderGrantHelper folderGrantHelper,
    SessionContext session,
    WizardLicenseChecker licenseChecker,
    IWindowsAccountService windowsAccountService,
    ILocalGroupMembershipService groupMembership,
    ILocalUserProvider localUserProvider,
    ISidResolver sidResolver,
    CredentialFilterHelper credentialFilterHelper,
    IAccountCredentialManager credentialManager,
    Func<WizardCredentialCollector> credentialCollectorFactory,
    IShortcutDiscoveryService discoveryService)
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

        var pickerStep = new AccountPickerStep(
            setSelection: (sid, isCreate) =>
            {
                data.ExistingAccountSid = sid;
                data.IsExistingAccount = !isCreate;
            },
            groupMembership: groupMembership,
            localUserProvider: localUserProvider,
            sidResolver: sidResolver,
            credentialFilterHelper: credentialFilterHelper,
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
                    getSid: () => data.IsExistingAccount ? data.ExistingAccountSid : null);

                if (isCreate)
                {
                    var nameStep = setupHelperFactory.CreateAccountNameStep(
                        (name, password) =>
                        {
                            data.Username = name;
                            data.Password?.Dispose();
                            var ss = new SecureString();
                            foreach (var c in password)
                                ss.AppendChar(c);
                            ss.MakeReadOnly();
                            data.Password = ss;
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
                var pw = credentialCollectorFactory().CollectIfNeeded(_data.ExistingAccountSid, session, progress);
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

        if (_data.GameLaunchers.Count > 0 &&
            !licenseChecker.CheckCanAddApps(session, _data.GameLaunchers.Count, progress))
            return;

        progress.ReportStatus($"Creating account '{_data.Username}'...");
        var passwordPtr = _data.Password != null
            ? Marshal.SecureStringToGlobalAllocUnicode(_data.Password)
            : IntPtr.Zero;
        string passwordText;
        try
        {
            if (passwordPtr != IntPtr.Zero)
            {
                var length = _data.Password!.Length;
                var passwordChars = new char[length];
                Marshal.Copy(passwordPtr, passwordChars, 0, length);
                passwordText = new string(passwordChars);
                // Zero the char array immediately. The resulting .NET string (passwordText) is
                // immutable and persists on the managed heap until GC — inherent limitation,
                // same as AccountCreationDefaults. The char[] is zeroed here to minimise exposure.
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(passwordChars.AsSpan()));
            }
            else
            {
                passwordText = string.Empty;
            }
        }
        finally
        {
            if (passwordPtr != IntPtr.Zero)
                Marshal.ZeroFreeGlobalAllocUnicode(passwordPtr);
        }

        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            Username: _data.Username,
            PasswordText: passwordText,
            ConfirmPasswordText: passwordText,
            IsEphemeral: false,
            CheckedGroups: [(GroupFilterHelper.UsersSid, "Users")],
            UncheckedGroups: [],
            AllowLogon: true,
            AllowNetworkLogin: false,
            AllowBgAutorun: false,
            CurrentHiddenCount: 0);

        var defaults = setupHelperFactory.CreateAccountDefaults();
        var setupOptions = new WizardSetupOptions(
            StoreCredential: true,
            IsEphemeral: false,
            PrivilegeLevel: PrivilegeLevel.Basic,
            FirewallSettings: new FirewallAccountSettings { AllowLan = false, AllowLocalhost = false },
            DesktopSettingsPath: defaults.DesktopSettingsPath,
            InstallPackages: null,
            TrayTerminal: false);

        var gameFolders = _data.GameFolders;
        var gameLaunchers = _data.GameLaunchers;

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

    private async Task ExecuteForExistingAccountAsync(IWizardProgressReporter progress)
    {
        if (string.IsNullOrEmpty(_data.ExistingAccountSid))
        {
            progress.ReportError("No account was selected.");
            return;
        }

        var sid = _data.ExistingAccountSid;

        // Pre-flight: only count launchers without an existing app entry for this account
        var existingExePaths = session.Database.Apps
            .Where(a => string.Equals(a.AccountSid, sid, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.ExePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newLauncherCount = _data.GameLaunchers
            .Count(p => !string.IsNullOrEmpty(p) && !existingExePaths.Contains(p));

        bool willAddCredential = _data.CollectedPassword != null;
        if (!licenseChecker.CheckCanAddCredential(session, progress, willAddCredential))
            return;
        if (newLauncherCount > 0 && !licenseChecker.CheckCanAddApps(session, newLauncherCount, progress))
            return;

        if (_data.CollectedPassword != null)
        {
            var (success, _, error) = credentialManager.AddNewCredential(
                sid, _data.CollectedPassword, session.CredentialStore, session.PinDerivedKey);
            if (!success && error != null)
                progress.ReportError($"Credential: {error}");
        }

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
            profilePath = windowsAccountService.GetProfilePath(sid);
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
            .ToList<AppEntryBuildOptions>();
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
        public SecureString? CollectedPassword { get; set; }
        public string Username { get; set; } = string.Empty;
        public SecureString? Password { get; set; }
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