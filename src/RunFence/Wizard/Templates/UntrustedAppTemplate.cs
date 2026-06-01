using RunFence.Account.UI;
using RunFence.Apps.UI;
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
internal class UntrustedAppTemplate(
    WizardTemplateExecutor executor,
    WizardAccountSetupHelperFactory setupHelperFactory,
    IAppContainerService appContainerService,
    IWizardSessionSaver sessionSaver,
    SessionContext session,
    WizardLicenseChecker licenseChecker,
    StandardAppWizardStepBuilder stepBuilder)
    : IWizardTemplate
{
    private readonly CommitData _data = new();

    public string DisplayName => "Untrusted App";
    public string Description => "Run an untrusted or risky app in a sandboxed account or AppContainer";
    public string IconEmoji => "\U0001F512"; // 🔒
    public Func<IWin32Window, Task>? PostWizardAction => null;

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
                    stepBuilder.CreateAppPathStep((path, name) =>
                    {
                        _data.AppPath = path;
                        _data.AppName = name;
                    }, appPathDesc,
                        initialPath: _data.AppPath,
                        initialName: _data.AppName)
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
                        defaultInternet: _data.AllowInternet,
                        defaultLan: _data.AllowLan,
                        defaultLocalhost: _data.AllowLocalhost),
                    stepBuilder.CreateAppPathStep((path, name) =>
                    {
                        _data.AppPath = path;
                        _data.AppName = name;
                    }, appPathDesc,
                        initialPath: _data.AppPath,
                        initialName: _data.AppName)
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
                defaultInternet: _data.AllowInternet,
                defaultLan: _data.AllowLan,
                defaultLocalhost: _data.AllowLocalhost),
            stepBuilder.CreateAppPathStep((path, name) =>
            {
                _data.AppPath = path;
                _data.AppName = name;
            }, appPathDesc,
                initialPath: _data.AppPath,
                initialName: _data.AppName)
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
        using var defaults = setupHelperFactory.CreateAccountDefaults();

        // Create account: remove from Users, optional low integrity and ephemeral, block all network
        progress.ReportStatus("Creating isolated account...");
        var request = EditAccountDialogCreateHandler.CreateAccountRequest.ForIsolatedAccount(
            defaults.Username, defaults.Password, isEphemeral: _data.IsEphemeral);

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

    private async Task ExecuteContainerAsync(IWizardProgressReporter progress)
    {
        // License checks
        if (!licenseChecker.CheckCanCreateContainer(session, progress))
            return;

        // Generate a deterministic unique container name from current config state
        var containerName = GenerateContainerName(_data.AppName, session.Database.AppContainers);

        var containerEntry = new AppContainerEntry
        {
            Name = containerName,
            DisplayName = _data.AppName,
            Capabilities = _data.ContainerCapabilities.Count > 0 ? _data.ContainerCapabilities : null,
            IsEphemeral = _data.IsEphemeral,
            DeleteAfterUtc = _data.IsEphemeral ? DateTime.UtcNow.AddHours(24) : null
        };

        session.Database.AppContainers.Add(containerEntry);
        sessionSaver.SaveConfig();

        // Create AppContainer profile
        progress.ReportStatus("Creating AppContainer profile...");
        try
        {
            var profileResult = await Task.Run(() => appContainerService.CreateProfile(containerEntry));
            if (profileResult.Status != AppContainerProfileSetupStatus.Succeeded)
            {
                progress.ReportError(profileResult.ErrorMessage ?? $"Container profile setup failed for '{containerEntry.Name}'.");
                return;
            }
        }
        catch (Exception ex)
        {
            progress.ReportError($"Container profile: {ex.Message}");
            return;
        }

        try
        {
            containerEntry.Sid = appContainerService.GetSid(containerEntry.Name);
            sessionSaver.SaveConfig();
        }
        catch
        {
        }

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

    private static string GenerateContainerName(string appName, IReadOnlyList<AppContainerEntry> existingContainers)
    {
        // Sanitize: keep only alphanumeric and hyphens, limit length.
        // Deterministic: no clock/random input; collision suffix uses stable ascending integer.
        var clean = new string(appName
                .Where(c => char.IsLetterOrDigit(c) || c == '-')
                .Take(32)
                .ToArray())
            .Trim('-');
        if (string.IsNullOrEmpty(clean))
            clean = "app";

        var baseName = $"rf-{clean.ToLowerInvariant()}";
        var existing = new HashSet<string>(
            existingContainers.Select(c => c.Name),
            StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(baseName))
            return baseName;

        var suffix = 2;
        while (existing.Contains($"{baseName}-{suffix}"))
            suffix++;

        return $"{baseName}-{suffix}";
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
            PrivilegeLevel = PrivilegeLevel.Isolated;
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
