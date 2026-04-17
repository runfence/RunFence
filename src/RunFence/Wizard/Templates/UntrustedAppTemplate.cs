using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.UI;
using RunFence.Wizard.UI.Forms;
using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard.Templates;

/// <summary>
/// Wizard template for running an untrusted application in a sandboxed environment.
/// Supports two isolation modes:
/// <list type="bullet">
/// <item><description>
///   <b>Account path</b>: isolated account removed from Users, optional Low Integrity and ephemeral,
///   all network blocked by default.
/// </description></item>
/// <item><description>
///   <b>Container path</b>: AppContainer with user-selected capabilities.
/// </description></item>
/// </list>
/// The step after <see cref="ContainerOrAccountStep"/> is dynamically swapped based on the selection:
/// <see cref="FirewallOptionsStep"/> for the account path, <see cref="ContainerCapabilitiesStep"/>
/// for the container path.
/// </summary>
public class UntrustedAppTemplate(
    WizardTemplateExecutor executor,
    WizardAccountSetupHelperFactory setupHelperFactory,
    IAppContainerService appContainerService,
    SessionContext session,
    WizardLicenseChecker licenseChecker,
    IShortcutDiscoveryService discoveryService)
    : IWizardTemplate
{
    private readonly CommitData _data = new();

    public string DisplayName => "Untrusted App";
    public string Description => "Run an untrusted or risky app in a sandboxed account or AppContainer";
    public string IconEmoji => "\U0001F512"; // 🔒
    public Action<IWin32Window>? PostWizardAction => null;

    public void Cleanup()
    {
    }

    public IReadOnlyList<WizardStepPage> CreateSteps()
    {
        _data.Reset();

        var containerOrAccountStep = new ContainerOrAccountStep((useContainer, privilegeLevel, isEphemeral) =>
        {
            _data.UseContainer = useContainer;
            _data.PrivilegeLevel = privilegeLevel;
            _data.IsEphemeral = isEphemeral;
        });

        // Wire dynamic step replacement: create fresh step instances each time the user toggles
        // between account and container mode. WizardDialog disposes replaced steps, so reusing
        // the same instances would lead to use-after-dispose when the user switches back.
        const string appPathDesc = "Select the untrusted application executable. " +
                                   "RunFence will create a desktop shortcut to launch it inside the sandbox.";

        containerOrAccountStep.SetBranchStepsProvider(isContainer =>
            isContainer
                ? [
                    new ContainerCapabilitiesStep(caps => _data.ContainerCapabilities = caps),
                    new AppPathStep((path, name) =>
                    {
                        _data.AppPath = path;
                        _data.AppName = name;
                    }, discoveryService, appPathDesc)
                ]
                :
                [
                    new FirewallOptionsStep(
                        (allowInternet, allowLan, allowLocalhost) =>
                        {
                            _data.AllowInternet = allowInternet;
                            _data.AllowLan = allowLan;
                            _data.AllowLocalhost = allowLocalhost;
                        },
                        defaultInternet: false,
                        defaultLan: false,
                        defaultLocalhost: false),
                    new AppPathStep((path, name) =>
                    {
                        _data.AppPath = path;
                        _data.AppName = name;
                    }, discoveryService, appPathDesc)
                ]);

        // Initial step list: account mode is default (account radio is pre-checked).
        return
        [
            containerOrAccountStep,
            new FirewallOptionsStep(
                (allowInternet, allowLan, allowLocalhost) =>
                {
                    _data.AllowInternet = allowInternet;
                    _data.AllowLan = allowLan;
                    _data.AllowLocalhost = allowLocalhost;
                },
                defaultInternet: false,
                defaultLan: false,
                defaultLocalhost: false),
            new AppPathStep((path, name) =>
            {
                _data.AppPath = path;
                _data.AppName = name;
            }, discoveryService, appPathDesc)
        ];
    }

    public async Task ExecuteAsync(IWizardProgressReporter progress)
    {
        if (string.IsNullOrEmpty(_data.AppPath) || string.IsNullOrEmpty(_data.AppName))
        {
            progress.ReportError("No application path was provided.");
            return;
        }

        if (_data.UseContainer)
            await ExecuteContainerAsync(progress);
        else
            await ExecuteAccountAsync(progress);
    }

    private async Task ExecuteAccountAsync(IWizardProgressReporter progress)
    {
        var defaults = setupHelperFactory.CreateAccountDefaults();

        // Create account: remove from Users, optional low integrity and ephemeral, block all network
        progress.ReportStatus("Creating isolated account...");
        var request = new EditAccountDialogCreateHandler.CreateAccountRequest(
            Username: defaults.Username,
            PasswordText: defaults.Password,
            ConfirmPasswordText: defaults.Password,
            IsEphemeral: _data.IsEphemeral,
            CheckedGroups: [],
            UncheckedGroups: [(GroupFilterHelper.UsersSid, "Users")],
            AllowLogon: false,
            AllowNetworkLogin: false,
            AllowBgAutorun: false,
            CurrentHiddenCount: 0);

        var firewallSettings = new FirewallAccountSettings
        {
            AllowInternet = _data.AllowInternet,
            AllowLan = _data.AllowLan,
            AllowLocalhost = _data.AllowLocalhost
        };

        var setupOptions = new WizardSetupOptions(
            StoreCredential: true,
            IsEphemeral: _data.IsEphemeral,
            PrivilegeLevel: _data.PrivilegeLevel,
            FirewallSettings: firewallSettings.IsDefault ? null : firewallSettings,
            DesktopSettingsPath: defaults.DesktopSettingsPath,
            InstallPackages: null,
            TrayTerminal: false);

        var appName = _data.AppName;
        var appPath = _data.AppPath;
        var privilegeLevel = _data.PrivilegeLevel;

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
                    manageShortcuts: true,
                    privilegeLevel: privilegeLevel)
            ],
            CreateDesktopShortcut: true);

        await executor.ExecuteAsync(flowParams, progress);
    }

    private async Task ExecuteContainerAsync(IWizardProgressReporter progress)
    {
        // License checks
        if (!licenseChecker.CheckCanCreateContainer(session, progress))
            return;
        if (!licenseChecker.CheckCanAddApp(session, progress))
            return;

        // Generate a unique container name from the app name
        var containerName = GenerateContainerName(_data.AppName);

        var containerEntry = new AppContainerEntry
        {
            Name = containerName,
            DisplayName = _data.AppName,
            Capabilities = _data.ContainerCapabilities.Count > 0 ? _data.ContainerCapabilities : null,
            IsEphemeral = _data.IsEphemeral,
            DeleteAfterUtc = _data.IsEphemeral ? DateTime.UtcNow.AddHours(24) : null
        };

        // Create AppContainer profile
        progress.ReportStatus("Creating AppContainer profile...");
        try
        {
            await Task.Run(() => appContainerService.CreateProfile(containerEntry));
        }
        catch (Exception ex)
        {
            progress.ReportError($"Container profile: {ex.Message}");
            return;
        }

        session.Database.AppContainers.Add(containerEntry);
        try { containerEntry.Sid = appContainerService.GetSid(containerEntry.Name); } catch { }

        var appName = _data.AppName;
        var appPath = _data.AppPath;

        // Use executor for app entry build + enforcement + save
        var flowParams = new WizardStandardFlowParams(
            Request: null,
            SetupOptions: null,
            BuildOptionsFactory: _ =>
            [
                AppEntryBuildOptions.ForWizard(
                    name: appName,
                    exePath: appPath,
                    accountSid: string.Empty,
                    restrictAcl: false,
                    aclMode: AclMode.Deny,
                    manageShortcuts: true,
                    appContainerName: containerName)
            ],
            CreateDesktopShortcut: true);

        await executor.ExecuteAsync(flowParams, progress);
    }

    private static string GenerateContainerName(string appName)
    {
        // Sanitize: keep only alphanumeric and hyphens, limit length
        var clean = new string(appName
                .Where(c => char.IsLetterOrDigit(c) || c == '-')
                .Take(32)
                .ToArray())
            .Trim('-');
        if (string.IsNullOrEmpty(clean))
            clean = "app";
        return $"rf-{clean.ToLowerInvariant()}-{DateTime.Now:yyMMddHHmm}";
    }

    private sealed class CommitData
    {
        public bool UseContainer { get; set; }
        public PrivilegeLevel PrivilegeLevel { get; set; }
        public bool IsEphemeral { get; set; }
        public bool AllowInternet { get; set; }
        public bool AllowLan { get; set; }
        public bool AllowLocalhost { get; set; }
        public List<string> ContainerCapabilities { get; set; } = [];
        public string AppPath { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;

        public void Reset()
        {
            UseContainer = false;
            PrivilegeLevel = PrivilegeLevel.Basic;
            IsEphemeral = false;
            AllowInternet = false;
            AllowLan = false;
            AllowLocalhost = false;
            ContainerCapabilities = [];
            AppPath = string.Empty;
            AppName = string.Empty;
        }
    }
}