using System.Security;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Infrastructure;
using RunFence.Account.UI.Forms;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Security;
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
///         <see cref="CredentialEditDialog"/> on the secure desktop (via <see cref="ISecureDesktopRunner"/>)
///         before advancing to <see cref="AppPathStep"/>.</item>
/// </list>
/// </summary>
public class ElevatedAppTemplate(
    WizardTemplateExecutor executor,
    WizardAccountSetupHelperFactory setupHelperFactory,
    IAccountCredentialManager credentialManager,
    IWindowsAccountService windowsAccountService,
    ILocalGroupMembershipService groupMembership,
    ILocalUserProvider localUserProvider,
    ISidResolver sidResolver,
    SessionContext session,
    WizardLicenseChecker licenseChecker,
    ISecureDesktopRunner secureDesktopRunner,
    Func<CredentialEditDialog> credentialEditDialogFactory)
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
            windowsAccountService: windowsAccountService,
            groupMembership: groupMembership,
            localUserProvider: localUserProvider,
            credentials: session.CredentialStore.Credentials,
            sidResolver: sidResolver,
            sidNames: session.Database.SidNames,
            groupSid: GroupFilterHelper.AdministratorsSid,
            stepTitle: "Select Administrator Account",
            infoText: "Select an existing administrator account, or choose \"Create new account\" to add a dedicated one. " +
                      "Accounts with a green dot have stored credentials; gray dot means you will be prompted for a password.",
            followingStepsFactory: isCreate =>
            {
                var appPathStep = new AppPathStep(
                    (path, name) =>
                    {
                        _data.AppPath = path;
                        _data.AppName = name;
                    },
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
            commitAction: CollectCredentialIfNeededAsync);

        var initialAppPathStep = new AppPathStep(
            (path, name) =>
            {
                _data.AppPath = path;
                _data.AppName = name;
            },
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

        Guid? credId = null;

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

            // For new admin accounts: use SetupOptions to store credential + set SplitTokenOptOut
            var setupOptions = new WizardSetupOptions(
                StoreCredential: true,
                IsEphemeral: false,
                SplitTokenOptOut: true, // full admin — no de-elevation
                LowIntegrityDefault: false,
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
            var (success, newCredId, error) = credentialManager.AddNewCredential(
                sid, _data.CollectedPassword, session.CredentialStore, session.PinDerivedKey);
            if (!success && error != null)
                progress.ReportError($"Credential: {error}");
            else
                credId = newCredId;
        }

        // Build and add app entry via executor
        if (string.IsNullOrEmpty(_data.AppPath) || string.IsNullOrEmpty(_data.AppName))
        {
            progress.ReportError("No application path was provided.");
            // Save any credential that was already stored for this account
            var saveOnlyParams = new WizardStandardFlowParams(
                Request: null,
                SetupOptions: null,
                AccountSid: sid,
                ExistingCredentialId: credId);
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
            ExistingCredentialId: credId,
            CreateDesktopShortcut: true);

        await executor.ExecuteAsync(existingFlowParams, progress);
    }

    /// <summary>
    /// Mid-wizard async hook: if the selected existing account has no stored credential,
    /// shows <see cref="CredentialEditDialog"/> on the secure desktop to collect the password.
    /// Returns without error when the user provides a valid password or when no password is needed.
    /// </summary>
    private async Task CollectCredentialIfNeededAsync(IWizardProgressReporter progress)
    {
        // Only needed for existing accounts without a stored credential
        if (_data.CreateNewAccount || string.IsNullOrEmpty(_data.SelectedExistingSid))
            return;

        var sid = _data.SelectedExistingSid;
        bool alreadyHasCredential = session.CredentialStore.Credentials
            .Any(c => string.Equals(c.Sid, sid, StringComparison.OrdinalIgnoreCase));

        if (alreadyHasCredential)
            return;

        // Collect password on secure desktop
        SecureString? collected = null;
        Exception? dialogException = null;

        var credEntry = new CredentialEntry { Id = Guid.NewGuid(), Sid = sid };

        try
        {
            secureDesktopRunner.Run(() =>
            {
                using var dlg = credentialEditDialogFactory();
                dlg.Initialize(existing: credEntry, hasStoredPassword: false,
                    sidNames: session.Database.SidNames);

                var dr = dlg.ShowDialog();
                if (dr == DialogResult.OK)
                    collected = dlg.Password;
            });
        }
        catch (Exception ex)
        {
            dialogException = ex;
        }

        if (dialogException != null)
        {
            progress.ReportError($"Credential dialog: {dialogException.Message}");
            throw new OperationCanceledException("Credential collection failed.", dialogException);
        }

        if (collected == null)
        {
            progress.ReportError("Password is required to use this account.");
            throw new OperationCanceledException("Password is required to use this account.");
        }

        _data.CollectedPassword = collected;
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