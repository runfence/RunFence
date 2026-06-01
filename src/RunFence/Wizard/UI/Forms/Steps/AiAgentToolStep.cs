using RunFence.Account;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
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
        IOpenFileDialogAdapterFactory openFileDialogFactory,
        IFolderBrowserDialogAdapterFactory folderBrowserDialogFactory,
        IAppDiscoveryDialogService appDiscoveryDialogService,
        Func<IWizardProgressReporter, Task>? commitAction = null)
    {
        _setOptions = setOptions;
        _commitAction = commitAction;
        BuildContent(discoveryService, iconHelper, openFileDialogFactory, folderBrowserDialogFactory, appDiscoveryDialogService);
    }

    public override Task OnCommitBeforeNextAsync(IWizardProgressReporter progress)
    {
        try
        {
            Collect();
        }
        catch (InvalidOperationException ex)
        {
            progress.ReportError(ex.Message);
            throw new WizardReportedException(ex.Message, ex);
        }

        return _commitAction != null ? _commitAction(progress) : Task.CompletedTask;
    }

    public override string StepTitle => "AI Agent Tool";
    public override string? Validate() => null;

    public override void Collect()
    {
        var useAiPackage = _aiPackageRadio.Checked;
        var appPath = useAiPackage
            ? null
            : NormalizeOptionalExecutablePathOrThrow(_appPathBrowseControl.PathText);
        if (!useAiPackage)
            _appPathBrowseControl.PathText = appPath ?? string.Empty;
        _setOptions(useAiPackage, appPath);
    }

    private void BuildContent(
        IShortcutDiscoveryService discoveryService,
        IShortcutIconHelper iconHelper,
        IOpenFileDialogAdapterFactory openFileDialogFactory,
        IFolderBrowserDialogAdapterFactory folderBrowserDialogFactory,
        IAppDiscoveryDialogService appDiscoveryDialogService)
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

        _appPathBrowseControl = new AppPathBrowseControl();
        _appPathBrowseControl.Initialize(
            openFileDialogFactory,
            folderBrowserDialogFactory,
            new AppPathBrowseConfiguration(
                "Select Tool Executable",
                "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                null,
                AppPathBrowseMode.File));
        _appPathBrowseControl.InitializeDiscovery(discoveryService, iconHelper, appDiscoveryDialogService);
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

    private static string? NormalizeOptionalExecutablePathOrThrow(string pathText)
    {
        var trimmed = pathText.Trim();
        if (trimmed.Length == 0)
            return null;

        if (!Path.IsPathRooted(trimmed))
            return trimmed;

        try
        {
            var normalizedPath = Path.GetFullPath(trimmed);
            if (!File.Exists(normalizedPath))
                throw new InvalidOperationException("The selected tool executable does not exist.");

            return normalizedPath;
        }
        catch (Exception ex)
        {
            throw ex is InvalidOperationException
                ? ex
                : new InvalidOperationException($"Tool path is invalid: {ex.Message}", ex);
        }
    }
}
