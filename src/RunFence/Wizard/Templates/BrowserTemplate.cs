using System.Security.AccessControl;
using RunFence.Account.UI;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core.Models;
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
public class BrowserTemplate(
    WizardTemplateExecutor executor,
    WizardAccountSetupHelperFactory setupHelperFactory,
    IHandlerMappingService mappingService,
    IAppHandlerRegistrationService registrationService,
    WizardFolderGrantHelper grantHelper)
    : IWizardTemplate
{
    private readonly CommitData _data = new();

    public string DisplayName => "Browser";
    public string Description => "Isolated browser account with selected folder access and URL handler associations";
    public string IconEmoji => "\U0001F310"; // 🌐
    public Action<IWin32Window>? PostWizardAction => null;

    private static readonly string[] BrowserAssociations = ["http", "https", ".htm", ".html"];

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
                description: "Choose a name for the new isolated browser account. " +
                             "The browser will run in this account, keeping it separated from your main session."),
            new AppPathStep(
                (path, name) =>
                {
                    _data.AppPath = path;
                    _data.AppName = name;
                },
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
            SplitTokenOptOut: false,
            LowIntegrityDefault: false,
            FirewallSettings: null,
            DesktopSettingsPath: defaults.DesktopSettingsPath,
            InstallPackages: null,
            TrayTerminal: false);

        var appName = _data.AppName;
        var appPath = _data.AppPath;
        var allowedPaths = _data.AllowedPaths;
        var handlerMappingService = mappingService;
        var handlerRegistrationService = registrationService;
        var folderGrantHelper = grantHelper;

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
            PreEnforcementAction: async (session, sid) =>
            {
                var readWriteRights = new SavedRightsState(
                    Execute: false, Write: true, Read: true, Special: false, Own: false);
                const FileSystemRights readWriteFileRights = FileSystemRights.ReadData | FileSystemRights.WriteData
                                                                                       | FileSystemRights.AppendData | FileSystemRights.ReadExtendedAttributes
                                                                                       | FileSystemRights.WriteExtendedAttributes | FileSystemRights.ReadAttributes
                                                                                       | FileSystemRights.WriteAttributes | FileSystemRights.ReadPermissions
                                                                                       | FileSystemRights.Synchronize;
                await folderGrantHelper.GrantFolderAccessAsync(
                    allowedPaths, sid, readWriteFileRights, readWriteRights, session, progress);
            },
            PostEnforcementAction: async (session, apps) =>
            {
                var app = apps.FirstOrDefault();
                if (app == null)
                    return;

                progress.ReportStatus("Registering handler associations...");
                try
                {
                    foreach (var key in BrowserAssociations)
                        handlerMappingService.SetHandlerMapping(key, app.Id, session.Database);

                    var effectiveMappings = handlerMappingService.GetEffectiveHandlerMappings(session.Database);
                    handlerRegistrationService.Sync(effectiveMappings, session.Database.Apps);
                }
                catch (Exception ex)
                {
                    progress.ReportError($"Handler associations: {ex.Message}");
                }

                await Task.CompletedTask;
            });

        await executor.ExecuteAsync(flowParams, progress);
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