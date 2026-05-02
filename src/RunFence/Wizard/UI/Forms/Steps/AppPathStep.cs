using System.Diagnostics;
using RunFence.Apps.Shortcuts;
using RunFence.Launching.Resolution;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step for picking an executable path and providing an optional display name for the app entry.
/// Auto-fills the app name from the file's product description or filename.
/// </summary>
internal class AppPathStep : WizardStepPage
{
    private readonly Action<string, string> _setPathAndName;
    private readonly IExecutablePathResolver _executablePathResolver;

    private Label _descriptionLabel = null!;
    private Label _pathLabel = null!;
    private AppPathBrowseControl _pathBrowseControl = null!;
    private Label _nameLabel = null!;
    private TextBox _appNameTextBox = null!;

    public AppPathStep(
        Action<string, string> setPathAndName,
        IShortcutDiscoveryService discoveryService,
        IShortcutIconHelper iconHelper,
        IExecutablePathResolver executablePathResolver,
        string? description = null,
        string? initialPath = null,
        string? initialName = null)
    {
        _setPathAndName = setPathAndName;
        _executablePathResolver = executablePathResolver;
        BuildContent(discoveryService, iconHelper, description, initialPath, initialName);
    }

    public override string StepTitle => "Application";

    public override string? Validate()
    {
        var path = _pathBrowseControl.PathText.Trim();
        if (path.Length == 0)
            return "Please select an application.";
        var resolved = ResolvePath(path);
        if (!File.Exists(resolved))
            return "The selected file does not exist.";
        if (_appNameTextBox.Text.Trim().Length == 0)
            return "Please enter an app name.";
        return null;
    }

    public override void Collect()
    {
        var path = _pathBrowseControl.PathText.Trim();
        var resolved = ResolvePath(path);
        _setPathAndName(resolved, _appNameTextBox.Text.Trim());
    }

    private void BuildContent(
        IShortcutDiscoveryService discoveryService,
        IShortcutIconHelper iconHelper,
        string? description,
        string? initialPath,
        string? initialName)
    {
        SuspendLayout();
        Padding = new Padding(8);

        bool hasDesc = !string.IsNullOrEmpty(description);

        _descriptionLabel = new Label
        {
            Text = description ?? string.Empty,
            AutoSize = false,
            Font = new Font("Segoe UI", 9.5f),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 8),
            Visible = hasDesc
        };
        if (hasDesc)
            TrackWrappingLabel(_descriptionLabel);

        _pathLabel = new Label
        {
            Text = "Application path:",
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 4)
        };

        _pathBrowseControl = new AppPathBrowseControl(discoveryService, iconHelper);
        _pathBrowseControl.Font = new Font("Segoe UI", 10);
        _pathBrowseControl.PathChanged += (_, _) => AutoFillAppName();

        _nameLabel = new Label
        {
            Text = "App name:",
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 8, 0, 4)
        };

        _appNameTextBox = new TextBox
        {
            Font = new Font("Segoe UI", 10),
            Dock = DockStyle.Top
        };

        // Add in reverse order so Dock=Top stacks top-to-bottom
        Controls.Add(_appNameTextBox);
        Controls.Add(_nameLabel);
        Controls.Add(_pathBrowseControl);
        Controls.Add(_pathLabel);
        if (hasDesc)
            Controls.Add(_descriptionLabel);
        ResumeLayout(false);

        if (!string.IsNullOrEmpty(initialPath))
            _pathBrowseControl.PathText = initialPath;
        if (!string.IsNullOrEmpty(initialName))
            _appNameTextBox.Text = initialName;
    }

    private void AutoFillAppName()
    {
        var path = _pathBrowseControl.PathText.Trim();
        var resolved = ResolvePath(path);
        if (!File.Exists(resolved))
            return;

        // Check if a name was auto-suggested by the Discover dialog
        var discoveredName = _pathBrowseControl.DiscoveredName;
        if (discoveredName != null && string.IsNullOrWhiteSpace(_appNameTextBox.Text))
        {
            _appNameTextBox.Text = discoveredName;
            return;
        }

        string suggestedName;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(resolved);
            suggestedName = !string.IsNullOrWhiteSpace(info.ProductName)
                ? info.ProductName.Trim()
                : Path.GetFileNameWithoutExtension(resolved);
        }
        catch
        {
            suggestedName = Path.GetFileNameWithoutExtension(resolved);
        }

        if (string.IsNullOrWhiteSpace(_appNameTextBox.Text))
            _appNameTextBox.Text = suggestedName;
    }

    private string ResolvePath(string path) =>
        _executablePathResolver.TryResolvePath(path, ExecutablePathResolutionContext.CurrentProcess()) ?? path;
}
