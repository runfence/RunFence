using System.ComponentModel;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Infrastructure;

namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Reusable UserControl that encapsulates a path textbox, Browse button, and optional Discover button
/// for selecting an application file or folder. Used by wizard steps that need app-path input.
/// </summary>
public partial class AppPathBrowseControl : UserControl
{
    private IShortcutDiscoveryService? _discoveryService;
    private IShortcutIconHelper? _iconHelper;
    private IAppDiscoveryDialogService? _appDiscoveryDialogService;
    private IOpenFileDialogAdapterFactory? _openFileDialogFactory;
    private IFolderBrowserDialogAdapterFactory? _folderBrowserDialogFactory;
    private AppPathBrowseConfiguration? _configuration;
    private bool _isInitialized;

    /// <summary>Raised when the path text changes.</summary>
    public event EventHandler? PathChanged;

    /// <summary>Gets or sets the current path in the textbox.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string PathText
    {
        get => _pathTextBox.Text;
        set => _pathTextBox.Text = value;
    }

    public void Initialize(
        IOpenFileDialogAdapterFactory openFileDialogFactory,
        IFolderBrowserDialogAdapterFactory folderBrowserDialogFactory,
        AppPathBrowseConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(openFileDialogFactory);
        ArgumentNullException.ThrowIfNull(folderBrowserDialogFactory);
        ArgumentNullException.ThrowIfNull(configuration);

        _openFileDialogFactory = openFileDialogFactory;
        _folderBrowserDialogFactory = folderBrowserDialogFactory;
        _configuration = configuration;
        _isInitialized = true;
        ApplyBrowseMode();
        UpdateButtonHeights();
    }

    public void InitializeDiscovery(
        IShortcutDiscoveryService discoveryService,
        IShortcutIconHelper iconHelper,
        IAppDiscoveryDialogService appDiscoveryDialogService)
    {
        ArgumentNullException.ThrowIfNull(discoveryService);
        ArgumentNullException.ThrowIfNull(iconHelper);
        ArgumentNullException.ThrowIfNull(appDiscoveryDialogService);

        _discoveryService = discoveryService;
        _iconHelper = iconHelper;
        _appDiscoveryDialogService = appDiscoveryDialogService;
        ApplyBrowseMode();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateButtonHeights();
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
        if (!_isInitialized || _configuration == null)
            return;

        if (_configuration.BrowseMode == AppPathBrowseMode.Folder)
        {
            BrowseFolder();
            return;
        }

        if (_openFileDialogFactory == null)
            return;

        using var dlgAdapter = _openFileDialogFactory.Create();
        var dlg = dlgAdapter.Dialog;
        dlg.Title = _configuration.DialogTitle;
        dlg.Filter = _configuration.FileFilter;
        dlg.CheckFileExists = true;

        var initialPath = !string.IsNullOrWhiteSpace(_pathTextBox.Text)
            ? _pathTextBox.Text
            : _configuration.InitialPath;
        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            var dir = Path.GetDirectoryName(initialPath);
            if (Directory.Exists(dir))
                dlg.InitialDirectory = dir;
        }

        if (dlgAdapter.ShowDialog(this) == DialogResult.OK)
        {
            DiscoveredName = null;
            _pathTextBox.Text = dlg.FileName;
        }
    }

    private async void OnDiscoverClick(object? sender, EventArgs e)
    {
        if (_configuration?.BrowseMode != AppPathBrowseMode.File
            || _discoveryService == null
            || _iconHelper == null
            || _appDiscoveryDialogService == null)
            return;

        _discoverButton.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            var apps = await Task.Run(() =>
                ShortcutClassificationHelper.ExcludeSystemExecutables(_discoveryService.DiscoverApps()));
            if (IsDisposed) return;

            var selection = _appDiscoveryDialogService.ShowDialog(apps, _iconHelper, this);
            if (selection != null)
            {
                DiscoveredName = selection.Value.name;
                _pathChangingFromDiscover = true;
                try { _pathTextBox.Text = selection.Value.path; }
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

    private void BrowseFolder()
    {
        if (_folderBrowserDialogFactory == null || _configuration == null)
            return;

        using var dlgAdapter = _folderBrowserDialogFactory.Create();
        var dlg = dlgAdapter.Dialog;
        dlg.Description = _configuration.DialogTitle;
        dlg.UseDescriptionForTitle = true;

        var initialPath = !string.IsNullOrWhiteSpace(_pathTextBox.Text)
            ? _pathTextBox.Text
            : _configuration.InitialPath;
        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
            dlg.InitialDirectory = initialPath;

        if (dlgAdapter.ShowDialog(this) == DialogResult.OK)
        {
            DiscoveredName = null;
            _pathTextBox.Text = dlg.SelectedPath;
        }
    }

    private void UpdateButtonHeights()
    {
        int inputHeight = _pathTextBox.PreferredHeight;
        _browseButton.Height = inputHeight;
        _discoverButton.Height = inputHeight;
        Height = inputHeight;
    }

    private void ApplyBrowseMode()
    {
        var isFileMode = _configuration?.BrowseMode != AppPathBrowseMode.Folder;
        _browseButton.Enabled = _isInitialized;
        _discoverButton.Visible = isFileMode;
        _discoverButton.Enabled = _isInitialized
            && isFileMode
            && _discoveryService != null
            && _iconHelper != null
            && _appDiscoveryDialogService != null;
    }
}
