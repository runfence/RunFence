using RunFence.Account;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI.Forms;
using RunFence.Infrastructure;

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
    private readonly IShortcutDiscoveryService _discoveryService;

    private RadioButton _aiPackageRadio = null!;
    private RadioButton _otherToolRadio = null!;
    private Label _appPathLabel = null!;
    private TextBox _appPathTextBox = null!;
    private Button _browseAppButton = null!;
    private Button _discoverAppButton = null!;
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
        Func<IWizardProgressReporter, Task>? commitAction = null)
    {
        _setOptions = setOptions;
        _discoveryService = discoveryService;
        _commitAction = commitAction;
        BuildContent();
    }

    public override Task OnCommitBeforeNextAsync(IWizardProgressReporter progress) =>
        _commitAction != null ? _commitAction(progress) : Task.CompletedTask;

    public override string StepTitle => "AI Agent Tool";
    public override string? Validate() => null;

    public override void Collect()
    {
        var useAiPackage = _aiPackageRadio.Checked;
        var appPath = !useAiPackage && !string.IsNullOrWhiteSpace(_appPathTextBox.Text)
            ? _appPathTextBox.Text.Trim()
            : null;
        _setOptions(useAiPackage, appPath);
    }

    private void BuildContent()
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

        _appPathTextBox = new TextBox
        {
            Font = new Font("Segoe UI", 9.5f),
            Dock = DockStyle.Fill
        };

        int inputHeight = _appPathTextBox.PreferredHeight;

        _browseAppButton = new Button
        {
            Text = "Browse…",
            Font = new Font("Segoe UI", 9),
            Width = 72,
            Height = inputHeight,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.System
        };
        _browseAppButton.Click += OnBrowseApp;

        _discoverAppButton = new Button
        {
            Text = "Discover…",
            Font = new Font("Segoe UI", 9),
            Width = 88,
            Height = inputHeight,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.System
        };
        _discoverAppButton.Click += OnDiscoverApp;

        var appPathRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = inputHeight
        };
        // Dock=Right: last added = highest Z-order = rightmost position.
        // Add Browse first (will be to the left), Discover last (rightmost), then Fill textbox.
        appPathRow.Controls.Add(_browseAppButton);
        appPathRow.Controls.Add(_discoverAppButton);
        appPathRow.Controls.Add(_appPathTextBox);

        int appPathPanelHeight = _appPathLabel.PreferredHeight + _appPathLabel.Padding.Vertical + inputHeight;
        _appPathPanel = new Panel { Dock = DockStyle.Top, Height = appPathPanelHeight, Visible = false };
        // Add in reverse order so Dock=Top stacks top-to-bottom inside _appPathPanel
        _appPathPanel.Controls.Add(appPathRow);
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

    private async void OnDiscoverApp(object? sender, EventArgs e)
    {
        _discoverAppButton.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            var apps = await Task.Run(() => _discoveryService.DiscoverApps());
            if (IsDisposed) return;

            using var dlg = new AppDiscoveryDialog(apps);
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _appPathTextBox.Text = dlg.SelectedPath;
        }
        finally
        {
            if (!IsDisposed)
            {
                Cursor = Cursors.Default;
                _discoverAppButton.Enabled = true;
            }
        }
    }

    private void OnBrowseApp(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog();
        dlg.Title = "Select Tool Executable";
        dlg.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
        dlg.CheckFileExists = true;
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _appPathTextBox.Text = dlg.FileName;
    }
}