using RunFence.Account;

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

    private RadioButton _claudeCodeRadio = null!;
    private RadioButton _otherToolRadio = null!;
    private Label _appPathLabel = null!;
    private TextBox _appPathTextBox = null!;
    private Button _browseAppButton = null!;
    private Panel _optionsPanel = null!;

    private const int RadioSectionHeight = 60;
    private const int AppPathSectionHeight = 58; // label 22 + gap 4 + row 27 + 5

    /// <param name="setOptions">Receives (isClaudeCode, appPath) on <see cref="Collect"/>.</param>
    /// <param name="commitAction">
    /// Mid-wizard async action run after <see cref="Collect"/> and before the wizard advances.
    /// Null = no mid-wizard work.
    /// </param>
    public AiAgentToolStep(
        Action<bool, string?> setOptions,
        Func<IWizardProgressReporter, Task>? commitAction = null)
    {
        _setOptions = setOptions;
        _commitAction = commitAction;
        BuildContent();
    }

    public override Task OnCommitBeforeNextAsync(IWizardProgressReporter progress) =>
        _commitAction != null ? _commitAction(progress) : Task.CompletedTask;

    public override string StepTitle => "AI Agent Tool";
    public override string? Validate() => null;

    public override void Collect()
    {
        var isClaudeCode = _claudeCodeRadio.Checked;
        var appPath = !isClaudeCode && !string.IsNullOrWhiteSpace(_appPathTextBox.Text)
            ? _appPathTextBox.Text.Trim()
            : null;
        _setOptions(isClaudeCode, appPath);
    }

    private void BuildContent()
    {
        SuspendLayout();
        Padding = new Padding(8);

        _claudeCodeRadio = new RadioButton
        {
            Text = $"Claude Code ({KnownPackages.ClaudeCode.DisplayName})",
            Font = new Font("Segoe UI", 10),
            AutoSize = true,
            Location = new Point(0, 0),
            Checked = true
        };
        _claudeCodeRadio.CheckedChanged += (_, _) => UpdateOtherToolVisibility();

        _otherToolRadio = new RadioButton
        {
            Text = "Other tool (specify executable below)",
            Font = new Font("Segoe UI", 10),
            AutoSize = true,
            Location = new Point(0, 26)
        };
        _otherToolRadio.CheckedChanged += (_, _) => UpdateOtherToolVisibility();

        _appPathLabel = new Label
        {
            Text = "Tool executable (optional — leave empty to use terminal):",
            AutoSize = false,
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(0, RadioSectionHeight),
            Height = 22,
            Visible = false
        };

        _appPathTextBox = new TextBox
        {
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(0, RadioSectionHeight + 26),
            Visible = false
        };

        _browseAppButton = new Button
        {
            Text = "Browse…",
            Font = new Font("Segoe UI", 9),
            Location = new Point(0, RadioSectionHeight + 26),
            Width = 72,
            Height = 27,
            FlatStyle = FlatStyle.System,
            Visible = false
        };
        _browseAppButton.Click += OnBrowseApp;

        _optionsPanel = new Panel { Dock = DockStyle.Top, Height = RadioSectionHeight };
        _optionsPanel.Controls.AddRange(_claudeCodeRadio, _otherToolRadio, _appPathLabel, _appPathTextBox, _browseAppButton);

        Controls.Add(_optionsPanel);
        ResumeLayout(false);
    }

    private void UpdateOtherToolVisibility()
    {
        var showOther = _otherToolRadio.Checked;
        _appPathLabel.Visible = showOther;
        _appPathTextBox.Visible = showOther;
        _browseAppButton.Visible = showOther;
        _optionsPanel.Height = showOther ? RadioSectionHeight + AppPathSectionHeight : RadioSectionHeight;

        if (showOther && _optionsPanel.Width > 0)
            PositionBrowseButton();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PositionBrowseButton();
    }

    private void PositionBrowseButton()
    {
        int available = _optionsPanel.ClientSize.Width;
        if (available <= 0)
            return;
        _appPathLabel.Width = available;
        _appPathTextBox.Width = available - _browseAppButton.Width - 8;
        _browseAppButton.Location = new Point(_appPathTextBox.Width + 8, _browseAppButton.Top);
    }

    private void OnBrowseApp(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog();
        dlg.Title = "Select Tool Executable";
        dlg.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
        dlg.CheckFileExists = true;
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _appPathTextBox.Text = dlg.FileName;
    }
}