using System.Security;
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
/// Wizard template for launching an existing application under a dedicated administrator account.
/// Covers two cases:
/// <list type="bullet">
///   <item>Create new account — AccountNameStep is inserted dynamically after <see cref="AccountPickerStep"/>
///         when the user selects "Create new account".</item>
///   <item>Use existing account — if the selected account has no stored credential the wizard shows
///         <see cref="CredentialEditDialog"/> on the secure desktop (via <see cref="WizardCredentialCollector"/>)
///         before advancing to <see cref="AppPathStep"/>.</item>
/// </list>
/// </summary>
public class ElevatedAppTemplate(
    WizardTemplateExecutor executor,
    WizardAccountSetupHelperFactory setupHelperFactory,
    IAccountCredentialManager credentialManager,
    ILocalGroupMembershipService groupMembership,
    ILocalUserProvider localUserProvider,
    ISidResolver sidResolver,
    CredentialFilterHelper credentialFilterHelper,
    SessionContext session,
    WizardLicenseChecker licenseChecker,
    Func<WizardCredentialCollector> credentialCollectorFactory,
    IShortcutDiscoveryService discoveryService)
    : IWizardTemplate
{
    private readonly CommitData _data = new();

    public string DisplayName => "Elevated App";
    public string Description => "Run an app under a dedicated administrator account with a desktop shortcut.";
    public string IconEmoji => "\U0001F6E0\uFE0F";
    public Action<IWin32Window>? PostWizardAction => null;

    public void Cleanup() => _data.CollectedPassword?.Dispose();

    public IReadOnlyList<WizardStepPage> CreateSteps()
    {
        // Reset commit data for each new template run (the same template instance may be
        // reused across multiple "Set up another" sessions in a single wizard session).
        _data.Reset();

        var pickerStep = new AccountPickerStep(
            setSelection: (sid, isCreate) =>
            {
                _data.SelectedExistingSid = sid;
                _data.CreateNewAccount = isCreate;
            },
            groupMembership: groupMembership,
            localUserProvider: localUserProvider,
            sidResolver: sidResolver,
            credentialFilterHelper: credentialFilterHelper,
            options: new AccountPickerStepOptions(
                Credentials: session.CredentialStore.Credentials,
                SidNames: session.Database.SidNames,
                GroupSid: GroupFilterHelper.AdministratorsSid,
                StepTitle: "Select Administrator Account",
                InfoText: "Select an existing administrator account, or choose \"Create new account\" to add a dedicated one. " +
                          "Accounts with a green dot have stored credentials; gray dot means you will be prompted for a password."),
            followingStepsFactory: isCreate =>
            {
                var appPathStep = new AppPathStep(
                    (path, name) =>
                    {
                        _data.AppPath = path;
                        _data.AppName = name;
                    },
                    discoveryService,
                    description: "Select the application to launch under the administrator account. " +
                                 "A desktop shortcut will be created to run it elevated.");

                if (isCreate)
                {
                    var nameStep = setupHelperFactory.CreateAccountNameStep(
                        (name, _) => _data.NewAccountName = name,
                        showPassword: false,
                        maxNameLength: 20,
                        description: "Choose a name for the new administrator account. " +
                                     "It will be added to the Administrators group and its credentials stored for use.");
                    return [nameStep, appPathStep];
                }

                return [appPathStep];
            },
            commitAction: progress =>
            {
                if (_data.CreateNewAccount || string.IsNullOrEmpty(_data.SelectedExistingSid))
                    return Task.CompletedTask;
                var pw = credentialCollectorFactory().CollectIfNeeded(_data.SelectedExistingSid, session, progress);
                if (pw != null) _data.CollectedPassword = pw;
                return Task.CompletedTask;
            });

        var initialAppPathStep = new AppPathStep(
            (path, name) =>
            {
                _data.AppPath = path;
                _data.AppName = name;
            },
            discoveryService,
            description: "Select the application to launch under the administrator account. " +
                         "A desktop shortcut will be created to run it elevated.");

        return [pickerStep, initialAppPathStep];
    }

    public async Task ExecuteAsync(IWizardProgressReporter progress)
    {
        // Only enforce credential limit when we will actually store a new credential:
        // always for new accounts, and for existing accounts only when a password was collected
        // (meaning the account had no stored credential and the user provided one).
        bool willAddCredential = _data.CreateNewAccount || _data.CollectedPassword != null;
        if (!licenseChecker.CheckCanAddCredential(session, progress, willAddCredential))
            return;
        if (!licenseChecker.CheckCanAddApp(session, progress))
            return;

        if (_data.CreateNewAccount)
        {
            // Create new administrator account
            if (string.IsNullOrEmpty(_data.NewAccountName))
            {
                progress.ReportError("No account name was provided.");
                return;
            }

            progress.ReportStatus($"Creating administrator account '{_data.NewAccountName}'...");

            var passwordChars = PasswordHelper.GenerateRandomPassword();
            string passwordText;
            try
            {
                passwordText = new string(passwordChars);
            }
            finally
            {
                Array.Clear(passwordChars, 0, passwordChars.Length);
            }

            var createRequest = new EditAccountDialogCreateHandler.CreateAccountRequest(
                Username: _data.NewAccountName,
                PasswordText: passwordText,
                ConfirmPasswordText: passwordText,
                IsEphemeral: false,
                CheckedGroups: [(GroupFilterHelper.AdministratorsSid, "Administrators")],
                UncheckedGroups: [(GroupFilterHelper.UsersSid, "Users")],
                AllowLogon: false,
                AllowNetworkLogin: false,
                AllowBgAutorun: false,
                CurrentHiddenCount: 0);

            // Use executor for account creation + app entry + enforcement + save
            if (string.IsNullOrEmpty(_data.AppPath) || string.IsNullOrEmpty(_data.AppName))
            {
                progress.ReportError("No application path was provided.");
                return;
            }

            var appName = _data.AppName;
            var appPath = _data.AppPath;

            // For new admin accounts: store credential + use HighestAllowed (no de-elevation)
            var setupOptions = new WizardSetupOptions(
                StoreCredential: true,
                IsEphemeral: false,
                PrivilegeLevel: PrivilegeLevel.HighestAllowed,
                FirewallSettings: null,
                DesktopSettingsPath: null,
                InstallPackages: null,
                TrayTerminal: false);

            var flowParams = new WizardStandardFlowParams(
                Request: createRequest,
                SetupOptions: setupOptions,
                BuildOptionsFactory: resolvedSid =>
                [
                    AppEntryBuildOptions.ForWizard(
                        name: appName,
                        exePath: appPath,
                        accountSid: resolvedSid,
                        restrictAcl: false,
                        aclMode: AclMode.Deny,
                        manageShortcuts: true)
                ],
                CreateDesktopShortcut: true);

            await executor.ExecuteAsync(flowParams, progress);
            return;
        }

        // Existing account path
        if (string.IsNullOrEmpty(_data.SelectedExistingSid))
        {
            progress.ReportError("No account was selected.");
            return;
        }

        var sid = _data.SelectedExistingSid;

        // Store credential if it was collected from the secure desktop dialog
        if (_data.CollectedPassword != null)
        {
            var (success, _, error) = credentialManager.AddNewCredential(
                sid, _data.CollectedPassword, session.CredentialStore, session.PinDerivedKey);
            if (!success && error != null)
                progress.ReportError($"Credential: {error}");
        }

        // Build and add app entry via executor
        if (string.IsNullOrEmpty(_data.AppPath) || string.IsNullOrEmpty(_data.AppName))
        {
            progress.ReportError("No application path was provided.");
            // Save any credential that was already stored for this account
            var saveOnlyParams = new WizardStandardFlowParams(
                Request: null,
                SetupOptions: null,
                AccountSid: sid);
            await executor.ExecuteAsync(saveOnlyParams, progress);
            return;
        }

        var existingAppName = _data.AppName;
        var existingAppPath = _data.AppPath;
        var existingSid = sid;

        var existingFlowParams = new WizardStandardFlowParams(
            Request: null,
            SetupOptions: null,
            AccountSid: existingSid,
            BuildOptionsFactory: _ =>
            [
                AppEntryBuildOptions.ForWizard(
                    name: existingAppName,
                    exePath: existingAppPath,
                    accountSid: existingSid,
                    restrictAcl: false,
                    aclMode: AclMode.Deny,
                    manageShortcuts: true)
            ],
            CreateDesktopShortcut: true);

        await executor.ExecuteAsync(existingFlowParams, progress);
    }

    private sealed class CommitData
    {
        public string? SelectedExistingSid { get; set; }
        public bool CreateNewAccount { get; set; }
        public string? NewAccountName { get; set; }
        public string? AppPath { get; set; }
        public string? AppName { get; set; }

        /// <summary>Password collected from <see cref="CredentialEditDialog"/> for an existing account without stored credentials.</summary>
        public SecureString? CollectedPassword { get; set; }

        public void Reset()
        {
            SelectedExistingSid = null;
            CreateNewAccount = false;
            NewAccountName = null;
            AppPath = null;
            AppName = null;
            CollectedPassword?.Dispose();
            CollectedPassword = null;
        }
    }
}