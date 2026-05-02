using RunFence.Account;
using RunFence.Apps.Shortcuts;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step for the AI Coding Agent template: selects between Claude Code and a custom tool,
/// and optionally specifies the custom executable path.
/// Holds the mid-wizard commit action that creates the account, installs packages,
/// and grants project folder access collected in the preceding step.
/// </summary>
public class AiAgentToolStep : WizardStepPage
{
    private readonly Action<bool, string?> _setOptions;
    private readonly Func<IWizardProgressReporter, Task>? _commitAction;

    private RadioButton _aiPackageRadio = null!;
    private RadioButton _otherToolRadio = null!;
    private Label _appPathLabel = null!;
    private AppPathBrowseControl _appPathBrowseControl = null!;
    private Panel _appPathPanel = null!;

    /// <param name="setOptions">Receives (useAiPackage, appPath) on <see cref="Collect"/>.</param>
    /// <param name="discoveryService">Used by the Discover button to find installed apps.</param>
    /// <param name="commitAction">
    /// Mid-wizard async action run after <see cref="Collect"/> and before the wizard advances.
    /// Null = no mid-wizard work.
    /// </param>
    public AiAgentToolStep(
        Action<bool, string?> setOptions,
        IShortcutDiscoveryService discoveryService,
        IShortcutIconHelper iconHelper,
        Func<IWizardProgressReporter, Task>? commitAction = null)
    {
        _setOptions = setOptions;
        _commitAction = commitAction;
        BuildContent(discoveryService, iconHelper);
    }

    public override Task OnCommitBeforeNextAsync(IWizardProgressReporter progress) =>
        _commitAction != null ? _commitAction(progress) : Task.CompletedTask;

    public override string StepTitle => "AI Agent Tool";
    public override string? Validate() => null;

    public override void Collect()
    {
        var useAiPackage = _aiPackageRadio.Checked;
        var appPath = !useAiPackage && !string.IsNullOrWhiteSpace(_appPathBrowseControl.PathText)
            ? _appPathBrowseControl.PathText.Trim()
            : null;
        _setOptions(useAiPackage, appPath);
    }

    private void BuildContent(IShortcutDiscoveryService discoveryService, IShortcutIconHelper iconHelper)
    {
        SuspendLayout();
        Padding = new Padding(8);

        _aiPackageRadio = new RadioButton
        {
            Text = KnownPackages.ClaudeCode.DisplayName,
            Font = new Font("Segoe UI", 10),
            AutoSize = true,
            Dock = DockStyle.Top,
            Checked = true
        };
        _aiPackageRadio.CheckedChanged += (_, _) => UpdateOtherToolVisibility();

        _otherToolRadio = new RadioButton
        {
            Text = "Other tool (specify executable below)",
            Font = new Font("Segoe UI", 10),
            AutoSize = true,
            Dock = DockStyle.Top
        };
        _otherToolRadio.CheckedChanged += (_, _) => UpdateOtherToolVisibility();

        _appPathLabel = new Label
        {
            Text = "Tool executable (optional — leave empty to use terminal):",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 8, 0, 4)
        };

        _appPathBrowseControl = new AppPathBrowseControl(
            discoveryService,
            iconHelper,
            dialogTitle: "Select Tool Executable");
        _appPathBrowseControl.Font = new Font("Segoe UI", 9);

        int appPathPanelHeight = _appPathLabel.PreferredHeight + _appPathLabel.Padding.Vertical + _appPathBrowseControl.Height;
        _appPathPanel = new Panel { Dock = DockStyle.Top, Height = appPathPanelHeight, Visible = false };
        // Add in reverse order so Dock=Top stacks top-to-bottom inside _appPathPanel
        _appPathPanel.Controls.Add(_appPathBrowseControl);
        _appPathPanel.Controls.Add(_appPathLabel);

        // Add in reverse order so Dock=Top stacks top-to-bottom
        Controls.Add(_appPathPanel);
        Controls.Add(_otherToolRadio);
        Controls.Add(_aiPackageRadio);
        ResumeLayout(false);
    }

    private void UpdateOtherToolVisibility()
    {
        _appPathPanel.Visible = !_aiPackageRadio.Checked;
    }
}
