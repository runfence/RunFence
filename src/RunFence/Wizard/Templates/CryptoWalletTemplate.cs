using RunFence.Account.UI;
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
internal class CryptoWalletTemplate(
    WizardTemplateExecutor executor,
    WizardAccountSetupHelperFactory setupHelperFactory,
    StandardAppWizardStepBuilder stepBuilder)
    : IWizardTemplate
{
    private readonly CommitData _data = new();

    public string DisplayName => "Crypto Wallet / Password Manager";
    public string Description => "Run a wallet or password manager in an isolated account with ACL protection.";
    public string IconEmoji => "\U0001F512";
    public Func<IWin32Window, Task>? PostWizardAction => null;

    public void Cleanup()
    {
    }

    public IReadOnlyList<WizardStepPage> CreateSteps()
    {
        _data.Reset();
        return
        [
            setupHelperFactory.CreateAccountNameStep(
                (name, password) => { _data.Username = name; password.Dispose(); },
                showPassword: false,
                maxNameLength: 20,
                description: "Choose a name for the new isolated account. " +
                             "The wallet or password manager will run in this account, " +
                             "protecting it from other processes on your system."),
            stepBuilder.CreateAppPathStep(
                (path, name) =>
                {
                    _data.AppPath = path;
                    _data.AppName = name;
                },
                description: "Select the wallet or password manager executable. " +
                             "A deny ACL will be placed on its parent folder to prevent accidental launch of a malicious replacement.")
        ];
    }

    public async Task ExecuteAsync(IWizardProgressReporter progress)
    {
        if (string.IsNullOrEmpty(_data.Username))
        {
            progress.ReportError("No account name was provided.");
            return;
        }

        if (string.IsNullOrEmpty(_data.AppPath) || string.IsNullOrEmpty(_data.AppName))
        {
            progress.ReportError("No application path was provided.");
            return;
        }

        using var defaults = setupHelperFactory.CreateAccountDefaults();

        progress.ReportStatus($"Creating account '{_data.Username}'...");
        var request = EditAccountDialogCreateHandler.CreateAccountRequest.ForIsolatedAccount(
            _data.Username, defaults.Password);

        var setupOptions = new WizardSetupOptions(
            StoreCredential: true,
            IsEphemeral: false,
            PrivilegeLevel: PrivilegeLevel.Isolated,
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

        try
        {
            await executor.ExecuteAsync(flowParams, progress);
        }
        finally
        {
            request.Password.Dispose();
            request.ConfirmPassword.Dispose();
        }
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
