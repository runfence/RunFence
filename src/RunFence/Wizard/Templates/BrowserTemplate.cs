using RunFence.Account.UI;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launching.Resolution;
using RunFence.Persistence;
using RunFence.UI;
using RunFence.Wizard.UI.Forms;
using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard.Templates;

/// <summary>
/// Wizard template for setting up an isolated browser account.
/// Creates an isolated account (removed from Users), stores credentials, grants read/write
/// access to allowed folders (downloads, documents, etc.), creates an app entry, and sets up
/// HTTP/HTTPS/HTML handler associations so clicking links opens the browser automatically.
/// </summary>
internal class BrowserTemplate(
    WizardTemplateExecutor executor,
    WizardAccountSetupHelperFactory setupHelperFactory,
    IHandlerMappingService mappingService,
    IAppHandlerRegistrationService registrationService,
    WizardFolderGrantHelper grantHelper,
    IShortcutDiscoveryService discoveryService,
    IShortcutIconHelper iconHelper,
    IExecutablePathResolver executablePathResolver)
    : IWizardTemplate
{
    private readonly CommitData _data = new();

    public string DisplayName => "Browser";
    public string Description => "Isolated browser account with selected folder access and URL handler associations";
    public string IconEmoji => "\U0001F310"; // 🌐
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
                (name, password) => { _data.Username = name; password.Dispose(); },
                description: "Choose a name for the new isolated browser account. " +
                             "The browser will run in this account, keeping it separated from your main session."),
            new AppPathStep(
                (path, name) =>
                {
                    _data.AppPath = path;
                    _data.AppName = name;
                },
                discoveryService,
                iconHelper,
                executablePathResolver,
                description: "Select the browser executable. The app name will appear in the RunFence app list " +
                             "and as the desktop shortcut label."),
            new AllowedPathsStep(
                paths => _data.AllowedPaths = paths,
                labelText: "Add folders the browser should be able to access (downloads, documents, etc.):"),
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
            PrivilegeLevel: PrivilegeLevel.Basic,
            FirewallSettings: new FirewallAccountSettings { AllowLan = true, AllowLocalhost = false },
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
                    restrictAcl: false,
                    aclMode: AclMode.Deny,
                    manageShortcuts: true)
            ],
            PreEnforcementAction: async (_, sid) =>
            {
                var readWriteRights = new SavedRightsState(
                    Execute: false, Write: true, Read: true, Special: false, Own: false);
                await grantHelper.GrantFolderAccessAsync(
                    _data.AllowedPaths, sid, readWriteRights, progress);
            },
            PostEnforcementAction: (sessionCtx, apps) =>
            {
                var app = apps.FirstOrDefault();
                if (app != null)
                {
                    progress.ReportStatus("Registering handler associations...");
                    try
                    {
                        foreach (var key in EvaluationConstants.BrowserAssociations)
                            mappingService.SetHandlerMapping(key, new HandlerMappingEntry(app.Id), sessionCtx.Database);

                        var effectiveMappings = mappingService.GetEffectiveHandlerMappings(sessionCtx.Database);
                        registrationService.Sync(effectiveMappings, sessionCtx.Database.Apps);
                    }
                    catch (Exception ex)
                    {
                        progress.ReportError($"Handler associations: {ex.Message}");
                    }
                }

                return Task.CompletedTask;
            },
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
        public string AppPath { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public List<string> AllowedPaths { get; set; } = [];

        public void Reset()
        {
            Username = string.Empty;
            AppPath = string.Empty;
            AppName = string.Empty;
            AllowedPaths = [];
        }
    }
}
