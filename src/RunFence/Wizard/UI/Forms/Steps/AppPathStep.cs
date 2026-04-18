using System.Diagnostics;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI.Forms;
using RunFence.Infrastructure;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step for picking an executable path and providing an optional display name for the app entry.
/// Auto-fills the app name from the file's product description or filename.
/// </summary>
public class AppPathStep : WizardStepPage
{
    private readonly Action<string, string> _setPathAndName;
    private readonly IShortcutDiscoveryService _discoveryService;

    private Label _descriptionLabel = null!;
    private Label _pathLabel = null!;
    private TextBox _pathTextBox = null!;
    private Button _browseButton = null!;
    private Button _discoverButton = null!;
    private Label _nameLabel = null!;
    private TextBox _appNameTextBox = null!;

    public AppPathStep(
        Action<string, string> setPathAndName,
        IShortcutDiscoveryService discoveryService,
        string? description = null,
        string? initialPath = null,
        string? initialName = null)
    {
        _setPathAndName = setPathAndName;
        _discoveryService = discoveryService;
        BuildContent(description, initialPath, initialName);
    }

    public override string StepTitle => "Application";

    public override string? Validate()
    {
        var path = _pathTextBox.Text.Trim();
        if (path.Length == 0)
            return "Please select an application.";
        if (!File.Exists(path))
            return "The selected file does not exist.";
        if (_appNameTextBox.Text.Trim().Length == 0)
            return "Please enter an app name.";
        return null;
    }

    public override void Collect()
    {
        _setPathAndName(_pathTextBox.Text.Trim(), _appNameTextBox.Text.Trim());
    }

    private void BuildContent(string? description, string? initialPath, string? initialName)
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

        _pathTextBox = new TextBox
        {
            Font = new Font("Segoe UI", 10),
            Dock = DockStyle.Fill
        };
        _pathTextBox.TextChanged += (_, _) => AutoFillAppName();

        int inputHeight = _pathTextBox.PreferredHeight;

        _browseButton = new Button
        {
            Text = "Browse…",
            Font = new Font("Segoe UI", 9),
            Width = 72,
            Height = inputHeight,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.System
        };
        _browseButton.Click += OnBrowseClick;

        _discoverButton = new Button
        {
            Text = "Discover…",
            Font = new Font("Segoe UI", 9),
            Width = 88,
            Height = inputHeight,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.System
        };
        _discoverButton.Click += OnDiscoverClick;

        var pathRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = inputHeight
        };
        // Dock=Right: last added = highest Z-order = rightmost position.
        // Add Browse first (will be to the left), Discover last (rightmost), then Fill textbox.
        pathRow.Controls.Add(_browseButton);
        pathRow.Controls.Add(_discoverButton);
        pathRow.Controls.Add(_pathTextBox);

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
        Controls.Add(pathRow);
        Controls.Add(_pathLabel);
        if (hasDesc)
            Controls.Add(_descriptionLabel);
        ResumeLayout(false);

        if (!string.IsNullOrEmpty(initialPath))
            _pathTextBox.Text = initialPath;
        if (!string.IsNullOrEmpty(initialName))
            _appNameTextBox.Text = initialName;
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog();
        dlg.Title = "Select Application";
        dlg.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
        dlg.CheckFileExists = true;
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);

        if (!string.IsNullOrWhiteSpace(_pathTextBox.Text))
        {
            var dir = Path.GetDirectoryName(_pathTextBox.Text);
            if (Directory.Exists(dir))
                dlg.InitialDirectory = dir;
        }

        if (dlg.ShowDialog(this) == DialogResult.OK)
            _pathTextBox.Text = dlg.FileName;
    }

    private async void OnDiscoverClick(object? sender, EventArgs e)
    {
        _discoverButton.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            var apps = await Task.Run(() => _discoveryService.DiscoverApps());
            if (IsDisposed) return;

            using var dlg = new AppDiscoveryDialog(apps);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _pathTextBox.Text = dlg.SelectedPath;
                if (string.IsNullOrWhiteSpace(_appNameTextBox.Text) && dlg.SelectedName != null)
                    _appNameTextBox.Text = dlg.SelectedName;
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                Cursor = Cursors.Default;
                _discoverButton.Enabled = true;
            }
        }
    }

    private void AutoFillAppName()
    {
        var path = _pathTextBox.Text.Trim();
        if (!File.Exists(path))
            return;

        string suggestedName;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            suggestedName = !string.IsNullOrWhiteSpace(info.ProductName)
                ? info.ProductName.Trim()
                : Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
            suggestedName = Path.GetFileNameWithoutExtension(path);
        }

        if (string.IsNullOrWhiteSpace(_appNameTextBox.Text))
            _appNameTextBox.Text = suggestedName;
    }
}
