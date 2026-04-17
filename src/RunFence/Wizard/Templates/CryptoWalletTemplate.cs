using RunFence.Account.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.UI;
using RunFence.Wizard.UI.Forms;
using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard.Templates;

/// <summary>
/// Wizard template for running a crypto wallet or password manager under an isolated account.
/// Creates a dedicated account, stores credentials, and creates an app entry with a deny ACL
/// on the parent folder to protect against accidental launch of a malicious replacement.
/// </summary>
public class CryptoWalletTemplate(
    WizardTemplateExecutor executor,
    WizardAccountSetupHelperFactory setupHelperFactory,
    IShortcutDiscoveryService discoveryService)
    : IWizardTemplate
{
    private readonly CommitData _data = new();

    public string DisplayName => "Crypto Wallet / Password Manager";
    public string Description => "Run a wallet or password manager in an isolated account with ACL protection.";
    public string IconEmoji => "\U0001F512";
    public Action<IWin32Window>? PostWizardAction => null;

    public void Cleanup()
    {
    }

    public IReadOnlyList<WizardStepPage> CreateSteps()
    {
        _data.Reset();
        return
        [
            setupHelperFactory.CreateAccountNameStep(
                (name, _) => _data.Username = name,
                showPassword: false,
                maxNameLength: 20,
                description: "Choose a name for the new isolated account. " +
                             "The wallet or password manager will run in this account, " +
                             "protecting it from other processes on your system."),
            new AppPathStep(
                (path, name) =>
                {
                    _data.AppPath = path;
                    _data.AppName = name;
                },
                discoveryService,
                description: "Select the wallet or password manager executable. " +
                             "A deny ACL will be placed on its parent folder to prevent accidental launch of a malicious replacement.")
        ];
    }

    public Task ExecuteAsync(IWizardProgressReporter progress)
    {
        if (string.IsNullOrEmpty(_data.Username))
        {
            progress.ReportError("No account name was provided.");
            return Task.CompletedTask;
        }

        if (string.IsNullOrEmpty(_data.AppPath) || string.IsNullOrEmpty(_data.AppName))
        {
            progress.ReportError("No application path was provided.");
            return Task.CompletedTask;
        }

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

        var setupOptions = new WizardSetupOptions(
            StoreCredential: true,
            IsEphemeral: false,
            PrivilegeLevel: PrivilegeLevel.Basic,
            FirewallSettings: null,
            DesktopSettingsPath: defaults.DesktopSettingsPath,
            InstallPackages: null,
            TrayTerminal: false);

        var appName = _data.AppName;
        var appPath = _data.AppPath;

        var flowParams = new WizardStandardFlowParams(
            Request: request,
            SetupOptions: setupOptions,
            BuildOptionsFactory: sid =>
            [
                AppEntryBuildOptions.ForWizard(
                    name: appName,
                    exePath: appPath,
                    accountSid: sid,
                    restrictAcl: true,
                    aclMode: AclMode.Deny,
                    manageShortcuts: true,
                    aclTarget: AclTarget.Folder)
            ],
            CreateDesktopShortcut: true);

        return executor.ExecuteAsync(flowParams, progress);
    }

    private sealed class CommitData
    {
        public string Username { get; set; } = string.Empty;
        public string? AppPath { get; set; }
        public string? AppName { get; set; }

        public void Reset()
        {
            Username = string.Empty;
            AppPath = null;
            AppName = null;
        }
    }
}
