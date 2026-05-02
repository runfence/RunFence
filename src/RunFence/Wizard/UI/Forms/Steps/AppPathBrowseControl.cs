using System.ComponentModel;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI.Forms;
using RunFence.Infrastructure;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Reusable UserControl that encapsulates a path textbox, Browse button, and Discover button
/// for selecting an executable. Used by wizard steps that need app-path input.
/// </summary>
public partial class AppPathBrowseControl : UserControl
{
    private readonly IShortcutDiscoveryService _discoveryService;
    private readonly IShortcutIconHelper _iconHelper;
    private readonly string _dialogTitle;
    private readonly string _dialogFilter;

    /// <summary>Raised when the path text changes.</summary>
    public event EventHandler? PathChanged;

    /// <summary>Gets or sets the current path in the textbox.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string PathText
    {
        get => _pathTextBox.Text;
        set => _pathTextBox.Text = value;
    }

    public AppPathBrowseControl(
        IShortcutDiscoveryService discoveryService,
        IShortcutIconHelper iconHelper,
        string dialogTitle = "Select Application",
        string dialogFilter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*")
    {
        _discoveryService = discoveryService;
        _iconHelper = iconHelper;
        _dialogTitle = dialogTitle;
        _dialogFilter = dialogFilter;

        InitializeComponent();

        // Set button heights to match the textbox preferred height (DPI-correct).
        int inputHeight = _pathTextBox.PreferredHeight;
        _browseButton.Height = inputHeight;
        _discoverButton.Height = inputHeight;
        Height = inputHeight;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        int inputHeight = _pathTextBox.PreferredHeight;
        _browseButton.Height = inputHeight;
        _discoverButton.Height = inputHeight;
        Height = inputHeight;
    }

    private bool _pathChangingFromDiscover;

    private void OnPathTextChanged(object? sender, EventArgs e)
    {
        if (!_pathChangingFromDiscover)
            DiscoveredName = null;
        PathChanged?.Invoke(this, e);
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog();
        dlg.Title = _dialogTitle;
        dlg.Filter = _dialogFilter;
        dlg.CheckFileExists = true;
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);

        if (!string.IsNullOrWhiteSpace(_pathTextBox.Text))
        {
            var dir = Path.GetDirectoryName(_pathTextBox.Text);
            if (Directory.Exists(dir))
                dlg.InitialDirectory = dir;
        }

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            DiscoveredName = null;
            _pathTextBox.Text = dlg.FileName;
        }
    }

    private async void OnDiscoverClick(object? sender, EventArgs e)
    {
        _discoverButton.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            var apps = await Task.Run(() => _discoveryService.DiscoverApps());
            if (IsDisposed) return;

            using var dlg = new AppDiscoveryDialog(apps, _iconHelper);
            if (await dlg.ShowDialogAsync(this) == DialogResult.OK)
            {
                DiscoveredName = dlg.SelectedName;
                _pathChangingFromDiscover = true;
                try { _pathTextBox.Text = dlg.SelectedPath; }
                finally { _pathChangingFromDiscover = false; }
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

    /// <summary>
    /// The display name from the last Discover dialog selection, or null if Browse was used since,
    /// the path was manually edited, or Discover has not yet been used.
    /// Callers may use this to auto-fill a name field.
    /// </summary>
    public string? DiscoveredName { get; private set; }
}
