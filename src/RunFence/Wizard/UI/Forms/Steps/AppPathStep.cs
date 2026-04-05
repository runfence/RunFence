using System.Diagnostics;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Wizard step for picking an executable path and providing an optional display name for the app entry.
/// Auto-fills the app name from the file's product description or filename.
/// </summary>
public class AppPathStep : WizardStepPage
{
    private readonly Action<string, string> _setPathAndName;

    private Label _descriptionLabel = null!;
    private Label _pathLabel = null!;
    private TextBox _pathTextBox = null!;
    private Button _browseButton = null!;
    private Label _nameLabel = null!;
    private TextBox _appNameTextBox = null!;

    private const int DescHeight = 52;
    private const int DescGap = 8;

    public AppPathStep(Action<string, string> setPathAndName, string? description = null)
    {
        _setPathAndName = setPathAndName;
        BuildContent(description);
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

    private void BuildContent(string? description)
    {
        SuspendLayout();
        Padding = new Padding(8);

        bool hasDesc = !string.IsNullOrEmpty(description);
        int offset = hasDesc ? DescHeight + DescGap : 0;

        _descriptionLabel = new Label
        {
            Text = description ?? string.Empty,
            AutoSize = false,
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(0, 0),
            Width = 540,
            Height = DescHeight,
            Visible = hasDesc
        };

        _pathLabel = new Label
        {
            Text = "Application path:",
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            Location = new Point(0, offset)
        };

        _pathTextBox = new TextBox
        {
            Font = new Font("Segoe UI", 10),
            Location = new Point(0, offset + 22),
            Width = 340
        };
        _pathTextBox.TextChanged += (_, _) => AutoFillAppName();

        _browseButton = new Button
        {
            Text = "Browse…",
            Font = new Font("Segoe UI", 9),
            Location = new Point(348, offset + 21),
            Width = 72,
            Height = 27,
            FlatStyle = FlatStyle.System
        };
        _browseButton.Click += OnBrowseClick;

        _nameLabel = new Label
        {
            Text = "App name:",
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            Location = new Point(0, offset + 58)
        };

        _appNameTextBox = new TextBox
        {
            Font = new Font("Segoe UI", 10),
            Location = new Point(0, offset + 80),
            Width = _browseButton.Right
        };

        Controls.AddRange(_descriptionLabel, _pathLabel, _pathTextBox, _browseButton, _nameLabel, _appNameTextBox);
        ResumeLayout(false);
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog();
        dlg.Title = "Select Application";
        dlg.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
        dlg.CheckFileExists = true;

        if (!string.IsNullOrWhiteSpace(_pathTextBox.Text))
        {
            var dir = Path.GetDirectoryName(_pathTextBox.Text);
            if (Directory.Exists(dir))
                dlg.InitialDirectory = dir;
        }

        if (dlg.ShowDialog(this) == DialogResult.OK)
            _pathTextBox.Text = dlg.FileName;
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