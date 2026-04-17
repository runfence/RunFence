using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.UI;

/// <summary>
/// Owns the folder browser exe path commit-on-leave pattern, browse button, arguments change,
/// and desktop settings path/export for <see cref="OptionsPanel"/>.
/// Wires parent-form events to flush the deferred exe-path commit before the form closes or minimizes.
/// </summary>
public class OptionsFolderBrowserSection
{
    private readonly OptionsFolderBrowserHandler _folderBrowserHandler;
    private readonly OptionsDesktopSettingsHandler _desktopSettingsHandler;
    private readonly ILaunchFacade _launchFacade;
    private readonly ILoggingService _log;
    private readonly IModalCoordinator _modalCoordinator;

    private TextBox _folderBrowserExeTextBox = null!;
    private TextBox _defaultSettingsPathTextBox = null!;
    private Button _exportSettingsButton = null!;
    private OperationGuard _operationGuard = null!;
    private Control _panelControl = null!;
    private Func<AppSettings> _getSettings = null!;
    private Action _debounceSave = null!;

    private Form? _parentForm;
    private bool _committingExePath;

    public OptionsFolderBrowserSection(
        IModalCoordinator modalCoordinator,
        OptionsFolderBrowserHandler folderBrowserHandler,
        OptionsDesktopSettingsHandler desktopSettingsHandler,
        ILaunchFacade launchFacade,
        ILoggingService log)
    {
        _modalCoordinator = modalCoordinator;
        _folderBrowserHandler = folderBrowserHandler;
        _desktopSettingsHandler = desktopSettingsHandler;
        _launchFacade = launchFacade;
        _log = log;
    }

    public void Initialize(
        TextBox folderBrowserExeTextBox,
        TextBox defaultSettingsPathTextBox,
        Button exportSettingsButton,
        OperationGuard operationGuard,
        Control panelControl,
        Func<AppSettings> getSettings,
        Action debounceSave)
    {
        _folderBrowserExeTextBox = folderBrowserExeTextBox;
        _defaultSettingsPathTextBox = defaultSettingsPathTextBox;
        _exportSettingsButton = exportSettingsButton;
        _operationGuard = operationGuard;
        _panelControl = panelControl;
        _getSettings = getSettings;
        _debounceSave = debounceSave;

        _folderBrowserExeTextBox.Leave += OnExePathLeave;
    }

    /// <summary>
    /// Wires parent-form events to flush the deferred exe-path commit.
    /// Call from the panel's <c>OnHandleCreated</c> after resolving the parent form.
    /// </summary>
    public void AttachParentForm(Form parentForm)
    {
        _parentForm = parentForm;
        _parentForm.Deactivate += OnParentFormCommitTrigger;
        _parentForm.FormClosing += OnParentFormCommitTrigger;
        _parentForm.SizeChanged += OnParentFormSizeChanged;
    }

    /// <summary>
    /// Unwires parent-form events and clears the reference.
    /// Call from the panel's <c>OnHandleDestroyed</c>.
    /// </summary>
    public void DetachParentForm()
    {
        if (_parentForm == null)
            return;
        _parentForm.Deactivate -= OnParentFormCommitTrigger;
        _parentForm.FormClosing -= OnParentFormCommitTrigger;
        _parentForm.SizeChanged -= OnParentFormSizeChanged;
        _parentForm = null;
    }

    public void BrowseExe()
    {
        var path = _folderBrowserHandler.BrowseExe();
        if (path != null)
        {
            _folderBrowserExeTextBox.Text = path;
            CommitFolderBrowserExePath();
        }
    }

    public void SetArguments(string args)
    {
        _folderBrowserHandler.SetArguments(args, _getSettings());
        _debounceSave();
    }

    public void SetDefaultSettingsPath(string path)
    {
        _desktopSettingsHandler.SetDesktopSettingsPath(path, _getSettings());
        _debounceSave();
    }

    public void BrowseDesktopSettings()
    {
        var path = _desktopSettingsHandler.BrowseDesktopSettings();
        if (path != null)
            _defaultSettingsPathTextBox.Text = path;
    }

    /// <summary>
    /// Simulates a click on the export settings button (for external callers).
    /// </summary>
    public void PerformExportSettings() => _exportSettingsButton.PerformClick();

    public async void ExportDesktopSettingsAsync()
    {
        using var dlg = new SaveFileDialog();
        dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
        dlg.DefaultExt = "json";
        dlg.FileName = "settings.json";
        dlg.Title = "Export Desktop Settings";
        try
        {
            Directory.CreateDirectory(Constants.ProgramDataDir);
            dlg.InitialDirectory = Constants.ProgramDataDir;
        }
        catch
        {
        }

        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);

        if (dlg.ShowDialog() != DialogResult.OK)
            return;

        var outputPath = dlg.FileName;
        _operationGuard.Begin(_panelControl);
        _modalCoordinator.BeginModal();
        try
        {
            await _desktopSettingsHandler.ExportAsync(outputPath);

            if (_panelControl.IsDisposed)
                return;

            if (string.IsNullOrEmpty(_defaultSettingsPathTextBox.Text))
                _defaultSettingsPathTextBox.Text = outputPath;

            var openForEdit = MessageBox.Show("Desktop settings exported successfully.\n\nOpen file for editing?",
                "Success", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (openForEdit == DialogResult.Yes)
                _launchFacade.LaunchFile(outputPath, AccountLaunchIdentity.InteractiveUser);
        }
        catch (Exception ex)
        {
            if (_panelControl.IsDisposed)
                return;
            _log.Error("Desktop settings export failed", ex);
            MessageBox.Show($"Export failed: {ex.Message}",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _modalCoordinator.EndModal();
            _operationGuard.End(_panelControl);
        }
    }

    private void OnExePathLeave(object? sender, EventArgs e) => CommitFolderBrowserExePath();

    private void OnParentFormCommitTrigger(object? sender, EventArgs e) => CommitFolderBrowserExePath();

    private void OnParentFormSizeChanged(object? sender, EventArgs e)
    {
        if (_parentForm?.WindowState == FormWindowState.Minimized)
            CommitFolderBrowserExePath();
    }

    private void CommitFolderBrowserExePath()
    {
        if (_committingExePath)
            return;
        _committingExePath = true;
        try
        {
            var error = _folderBrowserHandler.ValidateAndCommitExePath(_folderBrowserExeTextBox.Text, _getSettings());
            if (error != null)
            {
                MessageBox.Show(error, "Unsupported Application", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _folderBrowserExeTextBox.Text = _getSettings().FolderBrowserExePath;
                return;
            }

            _debounceSave();
        }
        finally
        {
            _committingExePath = false;
        }
    }
}
