using System.ComponentModel;
using RunFence.Acl.Permissions;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Infrastructure;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence.UI;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Persistence.UI.Forms;

/// <summary>
/// Config file management section: create, load, unload, export, and import app configs.
/// Used by OptionsPanel.
/// </summary>
public partial class ConfigManagerSection : UserControl
{
    private readonly IAppConfigService _appConfigService;
    private readonly ILoggingService _log;
    private readonly IAclPermissionService _aclPermission;
    private readonly ISessionProvider _sessionProvider;
    private readonly IConfigImportExportFilePicker _filePicker;
    private readonly ConfigExportController _exportController;
    private readonly MainConfigImportController _mainImportController;
    private readonly AdditionalConfigImportController _additionalImportController;
    private readonly IMessageBoxService _messageBoxService;

    public event Action<string>? ConfigLoadRequested;
    public event Action<string>? ConfigUnloadRequested;
    public event Action? DataChanged;

    public ConfigManagerSection(
        IAppConfigService appConfigService,
        IAclPermissionService aclPermission,
        ILoggingService log,
        ISessionProvider sessionProvider,
        IConfigImportExportFilePicker filePicker,
        ConfigExportController exportController,
        MainConfigImportController mainImportController,
        AdditionalConfigImportController additionalImportController,
        IMessageBoxService messageBoxService)
    {
        _appConfigService = appConfigService;
        _aclPermission = aclPermission;
        _log = log;
        _sessionProvider = sessionProvider;
        _filePicker = filePicker;
        _exportController = exportController;
        _mainImportController = mainImportController;
        _additionalImportController = additionalImportController;
        _messageBoxService = messageBoxService;
        InitializeComponent();
        _configDesc.Text = ContextHelpTextCatalog.ExtraConfig_InlineSummary;
        _configNewButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C4", Color.FromArgb(0x22, 0x8B, 0x22));
        _configLoadButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C2", Color.FromArgb(0xCC, 0x99, 0x00));
        _configUnloadButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4E4", Color.FromArgb(0xCC, 0x66, 0x00));
        _configExportButton.Image = UiIconFactory.CreateToolbarIcon("\u2191", Color.FromArgb(0x33, 0x66, 0x99));
        _configImportButton.Image = UiIconFactory.CreateToolbarIcon("\u2193", Color.FromArgb(0x33, 0x66, 0xCC));
    }

    private CredentialStore GetCredentialStore() => _sessionProvider.GetSession().CredentialStore;
    private ISecureSecretSnapshotSource GetPinDerivedKey() => _sessionProvider.GetSession().PinDerivedKey;

    public void RefreshConfigList()
    {
        _configListBox.Items.Clear();
        _configListBox.Items.Add(new ConfigComboItem(null));
        foreach (var path in _appConfigService.GetLoadedConfigPaths())
            _configListBox.Items.Add(new ConfigComboItem(path));
        if (_configListBox.Items.Count > 0)
            _configListBox.SelectedIndex = 0;
    }

    public void RegisterContextHelp(ContextHelpForm host)
    {
    }

    private void OnConfigDescResize(object? sender, EventArgs e)
    {
        if (_configDesc.Width <= 0)
            return;
        var h = TextRenderer.MeasureText(
            _configDesc.Text,
            _configDesc.Font,
            new Size(_configDesc.Width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
        if (_configDesc.Height != h)
            _configDesc.Height = h;
    }

    private void OnConfigSelectionChanged(object? sender, EventArgs e)
    {
        var item = _configListBox.SelectedItem as ConfigComboItem;
        _configUnloadButton.Enabled = item?.Path != null;
        _configImportButton.Enabled = item != null;
        _configExportButton.Enabled = item != null;
    }

    private void OnConfigMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            var index = _configListBox.IndexFromPoint(e.Location);
            _configListBox.SelectedIndex = index;
        }
    }

    private void OnConfigContextMenuOpening(object? sender, CancelEventArgs e)
    {
        var item = _configListBox.SelectedItem as ConfigComboItem;
        bool isAdditional = item?.Path != null;
        bool hasSelection = item != null;

        if (!hasSelection)
        {
            e.Cancel = true;
            return;
        }

        _ctxConfigUnload.Visible = isAdditional;
        _ctxConfigExportCtx.Visible = true;
        _ctxConfigImportCtx.Visible = true;
        _ctxConfigSepExport.Visible = isAdditional;
    }

    private void OnNewConfigClick(object? sender, EventArgs e)
    {
        var fileName = _filePicker.SelectSavePath(
            "RunFence Config (*.rfn)|*.rfn|All files (*.*)|*.*",
            "Create New App Config");
        if (string.IsNullOrEmpty(fileName))
            return;

        try
        {
            _appConfigService.CreateEmptyConfig(fileName, GetPinDerivedKey(), GetCredentialStore().ArgonSalt);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to create config", ex);
            _messageBoxService.Show($"Failed to create config: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (_messageBoxService.Show(
                "Restrict access to this config file to Administrators only?",
                "Set Permissions", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try
            {
                _aclPermission.RestrictToAdmins(fileName);
            }
            catch (Exception ex)
            {
                _log.Error("Failed to restrict config file permissions", ex);
                var loadUnrestricted = _messageBoxService.Show(
                    $"Failed to set permissions: {ex.Message}\n\nLoad the new config anyway without restricting it to Administrators only?",
                    "Set Permissions",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (loadUnrestricted != DialogResult.Yes)
                    return;
            }
        }

        ConfigLoadRequested?.Invoke(fileName);
    }

    private void OnLoadConfigClick(object? sender, EventArgs e)
    {
        var fileName = _filePicker.SelectOpenPath(
            "RunFence Config (*.rfn)|*.rfn|All files (*.*)|*.*",
            "Load App Config");
        if (string.IsNullOrEmpty(fileName))
            return;

        ConfigLoadRequested?.Invoke(fileName);
    }

    private void OnUnloadConfigClick(object? sender, EventArgs e)
    {
        if (_configListBox.SelectedItem is not ConfigComboItem item || item.Path == null)
        {
            _messageBoxService.Show("Select an additional config to unload.", "No Selection",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ConfigUnloadRequested?.Invoke(item.Path);
    }

    private void OnExportConfigClick(object? sender, EventArgs e)
    {
        if (_configListBox.SelectedItem is not ConfigComboItem item)
            return;

        var fileName = _filePicker.SelectSavePath(
            "JSON files (*.json)|*.json|All files (*.*)|*.*",
            "Export Config");
        if (string.IsNullOrEmpty(fileName))
            return;

        var result = _exportController.Export(item.Path, fileName);
        if (result.Succeeded)
        {
            _messageBoxService.Show($"Config exported to {result.ExportedFileName}.", "Export Complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            _messageBoxService.Show($"Export failed: {result.ErrorMessage}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnImportConfigClick(object? sender, EventArgs e)
    {
        if (_configListBox.SelectedItem is not ConfigComboItem item)
            return;

        var importPath = _filePicker.SelectOpenPath(
            "JSON files (*.json)|*.json|All files (*.*)|*.*",
            "Import Config");
        if (string.IsNullOrEmpty(importPath))
            return;

        var confirm = _messageBoxService.Show(
            "This will import plaintext config data and overwrite the current configuration.\n\nContinue?",
            "Confirm Import", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
            return;

        try
        {
            if (item.Path == null)
            {
                var result = await _mainImportController.ImportAsync(
                    importPath,
                    () => DataChanged?.Invoke(),
                    CancellationToken.None);
                RefreshConfigList();
                var warnings = result.Warnings.ToList();
                if (result.SaveError != null)
                {
                    var warningText = warnings.Count > 0
                        ? string.Join("\n", warnings)
                        : "None.";
                    _messageBoxService.Show(
                        $"Main config imported, but saving the updated state failed:\n{result.SaveError}\n\nWarnings:\n{warningText}",
                        "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (warnings.Count > 0)
                {
                    var warningText = string.Join("\n", warnings);
                    _messageBoxService.Show(
                        $"Main config imported with warnings:\n\n{warningText}",
                        "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    _messageBoxService.Show("Main config imported successfully.", "Import Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                var result = _additionalImportController.Import(importPath, item.Path);
                if (result.Succeeded)
                {
                    DataChanged?.Invoke();
                    RefreshConfigList();
                    _messageBoxService.Show("Config imported successfully.", "Import Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    var errorText = result.Errors.Count > 0
                        ? string.Join("\n", result.Errors)
                        : "Unknown import error.";
                    var message = $"Additional config import failed ({result.Status}): {errorText}";
                    _log.Error("Config import failed", new InvalidOperationException(message));
                    _messageBoxService.Show(
                        $"Import failed: {message}",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }
        catch (EvaluationLimitException ex)
        {
            _messageBoxService.Show(ex.Message, "Evaluation Limit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _log.Error("Config import failed", ex);
            _messageBoxService.Show($"Import failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
