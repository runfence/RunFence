using System.Diagnostics;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Launch;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.Launching.Resolution;
using RunFence.UI;
using RunFence.Wizard.UI.Forms;
using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard.Templates;

/// <summary>
/// Wizard template for setting up an isolated AI coding agent account (Claude Code or custom tool).
/// Creates an isolated account without Users group membership, grants access to project folders,
/// installs selected packages, applies the firewall rules chosen in the wizard (blocked by default),
/// and pins a tray terminal. After the wizard closes, if the wizard blocked Internet,
/// opens the firewall allowlist dialog with the blocked-connections dialog auto-opened inside it
/// so the user can whitelist required domains.
/// </summary>
internal class AiAgentTemplate(
    WizardTemplateExecutor executor,
    WizardAccountSetupHelperFactory setupHelperFactory,
    WizardFolderGrantHelper folderGrantHelper,
    AiAgentFirewallOrchestrator firewallOrchestrator,
    IWizardSessionSaver sessionSaver,
    SessionContext session,
    IShortcutDiscoveryService discoveryService,
    IShortcutIconHelper iconHelper,
    IExecutableKindService executableKindService)
    : IWizardTemplate
{
    private readonly CommitData _data = new();

    public string DisplayName => "AI Coding Agent";
    public string Description => "Isolated account for Claude Code or other AI tools with project folder access";
    public string IconEmoji => "\U0001F916"; // 🤖

    public Action<IWin32Window>? PostWizardAction { get; private set; }

    public void Cleanup()
    {
    }

    public IReadOnlyList<WizardStepPage> CreateSteps()
    {
        _data.Reset();

        var accountNameStep = setupHelperFactory.CreateAccountNameStep(
            (name, password) => { _data.Username = name; password.Dispose(); },
            description: "Choose a name for the new isolated AI agent account. " +
                         "It will be created without Users group membership. " +
                         "Required packages are installed during wizard setup, before firewall rules take effect.");

        var projectPathsStep = new AllowedPathsStep(
            paths => _data.ProjectPaths = paths,
            labelText: "Add project folders this account should be able to access:",
            stepTitle: "Project Folders");

        var firewallOptionsStep = new FirewallOptionsStep(
            (allowInternet, allowLan, allowLocalhost) =>
            {
                _data.AllowInternet = allowInternet;
                _data.AllowLan = allowLan;
                _data.AllowLocalhost = allowLocalhost;
            },
            defaultInternet: false,
            defaultLan: false,
            defaultLocalhost: false);

        var aiToolStep = new AiAgentToolStep(
            (useAiPackage, appPath) =>
            {
                _data.UseAiPackage = useAiPackage;
                _data.AppPath = appPath;
            },
            discoveryService,
            iconHelper,
            commitAction: progress =>
            {
                if (_data.UseAiPackage)
                    _data.AppPath = null;
                else if (string.IsNullOrWhiteSpace(_data.AppPath))
                    _data.AppPath = null;

                return Task.CompletedTask;
            });

        return [accountNameStep, projectPathsStep, firewallOptionsStep, aiToolStep];
    }

    public async Task ExecuteAsync(IWizardProgressReporter progress)
    {
        if (string.IsNullOrEmpty(_data.Username))
        {
            progress.ReportError("No account name was provided.");
            return;
        }

        using var defaults = setupHelperFactory.CreateAccountDefaults();

        var request = EditAccountDialogCreateHandler.CreateAccountRequest.ForIsolatedAccount(
            _data.Username,
            defaults.Password);

        var firewallSettings = new FirewallAccountSettings
        {
            AllowInternet = _data.AllowInternet,
            AllowLan = _data.AllowLan,
            AllowLocalhost = _data.AllowLocalhost
        };
        var packages = _data.UseAiPackage
            ? new List<InstallablePackage> { KnownPackages.WindowsTerminal, KnownPackages.ClaudeCode }
            : new List<InstallablePackage> { KnownPackages.WindowsTerminal };

        try
        {
            var flowParams = new WizardStandardFlowParams(
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
                BuildOptionsFactory: sid =>
                {
                    if (string.IsNullOrEmpty(_data.AppPath))
                        return [];

                    string appName;
                    try
                    {
                        var info = FileVersionInfo.GetVersionInfo(_data.AppPath);
                        appName = !string.IsNullOrWhiteSpace(info.FileDescription)
                            ? info.FileDescription
                            : Path.GetFileNameWithoutExtension(_data.AppPath);
                    }
                    catch
                    {
                        appName = Path.GetFileNameWithoutExtension(_data.AppPath);
                    }

                    return
                    [
                        AppEntryBuildOptions.ForWizard(
                            name: appName,
                            exePath: _data.AppPath,
                            accountSid: sid,
                            restrictAcl: false,
                            aclMode: AclMode.Deny,
                            manageShortcuts: true,
                            privilegeLevel: executableKindService.IsUwpExeFile(_data.AppPath)
                                            && session.Database.GetAccount(sid)?.PrivilegeLevel is not (PrivilegeLevel.Basic or PrivilegeLevel.HighestAllowed)
                                ? PrivilegeLevel.Basic
                                : null)
                    ];
                },
                PreEnforcementAction: async (_, sid) =>
                {
                    _data.CreatedSid = sid;
                    var readWriteSavedRights = new SavedRightsState(
                        Execute: false, Write: true, Read: true, Special: false, Own: false);

                    await folderGrantHelper.GrantFolderAccessAsync(
                        _data.ProjectPaths,
                        sid,
                        readWriteSavedRights,
                        progress);
                },
                CreateDesktopShortcut: !string.IsNullOrEmpty(_data.AppPath));

            await executor.ExecuteAsync(flowParams, progress);
        }
        finally
        {
            request.Password.Dispose();
            request.ConfirmPassword.Dispose();
        }

        var sid = _data.CreatedSid;
        if (string.IsNullOrEmpty(sid))
        {
            progress.ReportError("AI agent setup finished without a resolved account SID.");
            return;
        }

        var username = session.Database.SidNames.GetValueOrDefault(sid) ?? _data.Username;
        PostWizardAction = firewallOrchestrator.BuildPostWizardAction(
            sid,
            username,
            internetRestrictedInWizard: !_data.AllowInternet,
            session,
            sessionSaver,
            toolPath: _data.AppPath);
    }

    private sealed class CommitData
    {
        public string Username { get; set; } = string.Empty;
        public bool UseAiPackage { get; set; } = true;
        public List<string> ProjectPaths { get; set; } = [];
        public string? AppPath { get; set; }
        public string? CreatedSid { get; set; }
        public bool AllowInternet { get; set; }
        public bool AllowLan { get; set; }
        public bool AllowLocalhost { get; set; }

        public void Reset()
        {
            Username = string.Empty;
            UseAiPackage = true;
            ProjectPaths = [];
            AppPath = null;
            CreatedSid = null;
            AllowInternet = false;
            AllowLan = false;
            AllowLocalhost = false;
        }
    }
}
