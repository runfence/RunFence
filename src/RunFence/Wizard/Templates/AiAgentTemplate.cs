using System.Diagnostics;
using System.Security;
using System.Security.AccessControl;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.UI;
using RunFence.Wizard.UI.Forms;
using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard.Templates;

/// <summary>
/// Wizard template for setting up an isolated AI coding agent account (Claude Code or custom tool).
/// Creates an isolated account without Users group membership, grants access to project folders,
/// installs selected packages, applies restrictive firewall rules (no internet, no LAN, localhost only),
/// and pins a tray terminal. After the wizard closes, opens the firewall allowlist dialog and blocked
/// connections dialog so the user can whitelist required domains immediately.
/// </summary>
public class AiAgentTemplate(
    WizardTemplateExecutor executor,
    WizardAccountSetupHelperFactory setupHelperFactory,
    EditAccountDialogCreateHandler createHandler,
    AccountLauncher accountLauncher,
    WizardFolderGrantHelper folderGrantHelper,
    AiAgentFirewallOrchestrator firewallOrchestrator,
    IWizardSessionSaver sessionSaver,
    SessionContext session,
    WizardLicenseChecker licenseChecker)
    : IWizardTemplate
{
    private readonly CommitData _data = new();

    public string DisplayName => "AI Coding Agent";
    public string Description => "Isolated account for Claude Code or other AI tools with project folder access";
    public string IconEmoji => "\U0001F916"; // 🤖

    public Action<IWin32Window>? PostWizardAction { get; private set; }

    public void Cleanup()
    {
        _data.CreatedPassword?.Dispose();
        _data.CreatedPassword = null;
    }

    public IReadOnlyList<WizardStepPage> CreateSteps()
    {
        _data.Reset();

        var accountNameStep = setupHelperFactory.CreateAccountNameStep(
            (name, _) => _data.Username = name,
            description: "Choose a name for the new isolated AI agent account. " +
                         "It will be created without Users group membership and without internet access. " +
                         "Required packages are installed before internet is blocked.");

        var projectPathsStep = new AllowedPathsStep(
            paths => _data.ProjectPaths = paths,
            labelText: "Add project folders this account should be able to access:",
            stepTitle: "Project Folders");

        var aiToolStep = new AiAgentToolStep(
            (isClaudeCode, appPath) =>
            {
                _data.IsClaudeCode = isClaudeCode;
                _data.AppPath = appPath;
            },
            commitAction: OnCommitAiOptionsAsync);

        return [accountNameStep, projectPathsStep, aiToolStep];
    }

    /// <summary>
    /// Mid-wizard hook: creates the account, grants project folder access, installs packages.
    /// Called after AiAgentOptionsStep is committed. Firewall is applied in ExecuteAsync.
    /// Account creation is done directly here (not via executor) because this hook must complete
    /// synchronously within the wizard step before the user can advance.
    /// </summary>
    private async Task OnCommitAiOptionsAsync(IWizardProgressReporter progress)
    {
        if (string.IsNullOrEmpty(_data.Username))
        {
            progress.ReportError("No account name was provided.");
            throw new OperationCanceledException("No account name was provided.");
        }

        // License check
        if (!licenseChecker.CheckCanAddCredential(session, progress))
            throw new OperationCanceledException("License check failed.");

        var defaults = setupHelperFactory.CreateAccountDefaults();

        progress.ReportStatus($"Creating account '{_data.Username}'...");
        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            Username: _data.Username,
            PasswordText: defaults.Password,
            ConfirmPasswordText: defaults.Password,
            IsEphemeral: false,
            CheckedGroups: [],
            UncheckedGroups: [(GroupFilterHelper.UsersSid, "Users")],
            AllowLogon: false,
            AllowNetworkLogin: false,
            AllowBgAutorun: false,
            CurrentHiddenCount: 0);

        var result = await Task.Run(() => createHandler.Execute(request));
        if (result == null)
        {
            var error = createHandler.LastValidationError ?? "Account creation failed.";
            progress.ReportError(error);
            throw new OperationCanceledException(error);
        }

        _data.CreatedSid = result.Sid;
        _data.CreatedPassword = result.Password;
        foreach (var err in result.Errors)
            progress.ReportError(err);

        // Setup: store credential, SidNames, AccountEntry; no firewall yet (done in ExecuteAsync).
        var setupHelper = setupHelperFactory.Create(session);
        var setupRequest = new WizardAccountSetupHelper.SetupRequest(
            Sid: result.Sid,
            Username: result.Username,
            Password: result.Password,
            StoreCredential: true,
            IsEphemeral: false,
            SplitTokenOptOut: false,
            LowIntegrityDefault: false,
            FirewallSettings: null,
            DesktopSettingsPath: defaults.DesktopSettingsPath,
            InstallPackages: null,
            TrayTerminal: false);

        await setupHelper.SetupAsync(setupRequest, progress);

        // Install packages and wait for completion before advancing.
        // Firewall rules that block internet will be applied in ExecuteAsync — we must ensure
        // software is fully installed before internet access is cut off.
        List<InstallablePackage> packages = _data.IsClaudeCode
            ? [KnownPackages.Winget, KnownPackages.WindowsTerminal, KnownPackages.ClaudeCode]
            : [KnownPackages.Winget, KnownPackages.WindowsTerminal];

        var credEntry = session.CredentialStore.Credentials.FirstOrDefault(c => c.Sid == result.Sid);
        if (credEntry != null)
        {
            progress.ReportStatus("Installing packages (internet will be blocked after setup completes)...");
            await Task.Run(() => accountLauncher.InstallPackages(packages, result.Password, credEntry, session.Database.SidNames));
            await accountLauncher.WaitForInstallCompletionAsync(result.Sid, TimeSpan.FromMinutes(10));
        }

        // Persist before granting project folder access so grant tracking has a DB entry
        sessionSaver.SaveAndRefresh();

        // Grant project folder access — uses WizardFolderGrantHelper to track SavedRightsState
        const FileSystemRights readWriteRights =
            FileSystemRights.ReadData |
            FileSystemRights.WriteData |
            FileSystemRights.ReadAndExecute |
            FileSystemRights.Synchronize;
        var readWriteSavedRights = new SavedRightsState(
            Execute: false, Write: true, Read: true, Special: false, Own: false);

        await folderGrantHelper.GrantFolderAccessAsync(
            _data.ProjectPaths, result.Sid, readWriteRights, readWriteSavedRights, session, progress);
    }

    public async Task ExecuteAsync(IWizardProgressReporter progress)
    {
        if (_data.CreatedSid == null)
        {
            progress.ReportError("Account was not created in the previous step.");
            throw new OperationCanceledException("Account was not created in the previous step.");
        }

        var sid = _data.CreatedSid;
        var db = session.Database;
        var username = db.SidNames.GetValueOrDefault(sid) ?? _data.Username;

        // Apply restrictive firewall settings now that packages are installed.
        // Firewall is always restricted after wizard — user can refine via post-wizard allowlist dialog.
        var firewallSettings = new FirewallAccountSettings
        {
            AllowInternet = false,
            AllowLan = false,
            AllowLocalhost = true
        };

        if (!firewallSettings.IsDefault)
            await firewallOrchestrator.ApplyRestrictiveRulesAsync(sid, username, firewallSettings, progress);

        // Enable tray terminal for this account
        db.GetOrCreateAccount(sid).TrayTerminal = true;

        // Create optional app entry for a custom tool path
        if (!string.IsNullOrEmpty(_data.AppPath) && licenseChecker.CheckCanAddApp(session, progress))
        {
            var appPath = _data.AppPath;
            var appName = ResolveAppName(appPath);

            var flowParams = new WizardStandardFlowParams(
                Request: null,
                SetupOptions: null,
                AccountSid: sid,
                BuildOptionsFactory: _ =>
                [
                    AppEntryBuildOptions.ForWizard(
                        name: appName,
                        exePath: appPath,
                        accountSid: sid,
                        restrictAcl: false,
                        aclMode: AclMode.Deny,
                        manageShortcuts: true)
                ]);

            await executor.ExecuteAsync(flowParams, progress);
        }
        else
        {
            progress.ReportStatus("Done.");
            sessionSaver.SaveAndRefresh();
        }

        // Set post-wizard action: open firewall allowlist dialog so the user can whitelist required
        // domains, then open blocked connections dialog with audit logging pre-enabled so they can
        // see what the AI agent is trying to reach.
        PostWizardAction = firewallOrchestrator.BuildPostWizardAction(sid, username, session, sessionSaver);
    }

    private static string ResolveAppName(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            if (!string.IsNullOrWhiteSpace(info.FileDescription))
                return info.FileDescription;
        }
        catch
        {
        }

        return Path.GetFileNameWithoutExtension(exePath);
    }

    private sealed class CommitData
    {
        public string Username { get; set; } = string.Empty;
        public bool IsClaudeCode { get; set; } = true;
        public List<string> ProjectPaths { get; set; } = [];
        public string? AppPath { get; set; }
        public string? CreatedSid { get; set; }
        public SecureString? CreatedPassword { get; set; }

        public void Reset()
        {
            Username = string.Empty;
            IsClaudeCode = true;
            ProjectPaths = [];
            AppPath = null;
            CreatedSid = null;
            CreatedPassword?.Dispose();
            CreatedPassword = null;
        }
    }
}