using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
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
    WizardTemplateSetupBuilder setupBuilder,
    AiAgentFirewallOrchestrator firewallOrchestrator,
    IWizardSessionSaver sessionSaver,
    SessionContext session,
    AiAgentWizardStepBuilder stepBuilder)
    : IWizardTemplate
{
    private readonly AiAgentTemplateState _data = new();

    public string DisplayName => "AI Coding Agent";
    public string Description => "Isolated account for Claude Code or other AI tools with project folder access";
    public string IconEmoji => "\U0001F916"; // 🤖

    public Func<IWin32Window, Task>? PostWizardAction { get; private set; }

    public void Cleanup()
    {
    }

    public IReadOnlyList<WizardStepPage> CreateSteps()
    {
        _data.Reset();

        var accountNameStep = stepBuilder.CreateAccountNameStep(
            (name, password) => { _data.Username = name; password.Dispose(); },
            description: "Choose a name for the new isolated AI agent account. " +
                         "It will be created without Users group membership. " +
                         "Required packages are installed during wizard setup, before firewall rules take effect.");

        var projectPathsStep = stepBuilder.CreateProjectFoldersStep(
            paths => _data.ProjectPaths = paths);

        var firewallOptionsStep = stepBuilder.CreateFirewallOptionsStep(
            (allowInternet, allowLan, allowLocalhost) =>
            {
                _data.AllowInternet = allowInternet;
                _data.AllowLan = allowLan;
                _data.AllowLocalhost = allowLocalhost;
            },
            defaultInternet: false,
            defaultLan: false,
            defaultLocalhost: false);

        var aiToolStep = stepBuilder.CreateToolStep(
            (useAiPackage, appPath) =>
            {
                _data.UseAiPackage = useAiPackage;
                _data.AppPath = appPath;
            },
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

        var flowParams = setupBuilder.BuildAiAgentFlow(_data, progress);

        try
        {
            await executor.ExecuteAsync(flowParams, progress);
        }
        finally
        {
            flowParams.Request?.Password.Dispose();
            flowParams.Request?.ConfirmPassword.Dispose();
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
}
