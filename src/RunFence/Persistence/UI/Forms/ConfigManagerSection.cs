using System.ComponentModel;
using System.Text.Json;
using RunFence.Account;
using RunFence.Acl.Permissions;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Infrastructure;
using RunFence.Core.Models;
using RunFence.Infrastructure;
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
    private readonly IAppFilter _appFilter;
    private readonly ILoggingService _log;
    private readonly IAclPermissionService _aclPermission;
    private readonly ConfigImportHandler _importHandler;
    private readonly AdditionalConfigImportCoordinator _additionalImportCoordinator;
    private readonly ISessionProvider _sessionProvider;
    private readonly IAccountSidResolutionService _sidResolutionService;
    private readonly HandlerSyncHelper _handlerSyncHelper;
    private readonly IMessageBoxService _messageBoxService;

    /// <summary>Fired when a config file should be loaded by the parent.</summary>
    public event Action<string>? ConfigLoadRequested;

    /// <summary>Fired when a config file should be unloaded by the parent.</summary>
    public event Action<string>? ConfigUnloadRequested;

    /// <summary>Fired when config data changes and the parent should refresh.</summary>
    public event Action? DataChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<string, string, string?> SaveFilePathSelector { get; set; } = SelectSavePath;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<string, string, string?> OpenFilePathSelector { get; set; } = SelectOpenPath;

    public ConfigManagerSection(IAppConfigService appConfigService,
        IAppFilter appFilter, ILoggingService log, IAclPermissionService aclPermission,
        ConfigImportHandler importHandler, AdditionalConfigImportCoordinator additionalImportCoordinator,
        ISessionProvider sessionProvider,
        IAccountSidResolutionService sidResolutionService,
        HandlerSyncHelper handlerSyncHelper,
        IMessageBoxService messageBoxService)
    {
        _appConfigService = appConfigService;
        _appFilter = appFilter;
        _log = log;
        _aclPermission = aclPermission;
        _importHandler = importHandler;
        _additionalImportCoordinator = additionalImportCoordinator;
        _sessionProvider = sessionProvider;
        _sidResolutionService = sidResolutionService;
        _handlerSyncHelper = handlerSyncHelper;
        _messageBoxService = messageBoxService;
        InitializeComponent();
        _configDesc.Text = ContextHelpTextCatalog.ExtraConfig_InlineSummary;
        _configNewButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C4", Color.FromArgb(0x22, 0x8B, 0x22));
        _configLoadButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C2", Color.FromArgb(0xCC, 0x99, 0x00));
        _configUnloadButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4E4", Color.FromArgb(0xCC, 0x66, 0x00));
        _configExportButton.Image = UiIconFactory.CreateToolbarIcon("\u2191", Color.FromArgb(0x33, 0x66, 0x99));
        _configImportButton.Image = UiIconFactory.CreateToolbarIcon("\u2193", Color.FromArgb(0x33, 0x66, 0xCC));
    }

    private AppDatabase GetDatabase() => _sessionProvider.GetSession().Database;
    private CredentialStore GetCredentialStore() => _sessionProvider.GetSession().CredentialStore;
    private ISecureSecretSnapshotSource GetPinDerivedKey()
        => _sessionProvider.GetSession().PinDerivedKey;

    public void RefreshConfigList()
    {
        _configListBox.Items.Clear();
        _configListBox.Items.Add(new ConfigComboItem(null)); // "Main Config"
        foreach (var path in _appConfigService.GetLoadedConfigPaths())
            _configListBox.Items.Add(new ConfigComboItem(path));
        if (_configListBox.Items.Count > 0)
            _configListBox.SelectedIndex = 0;
    }

    public void RegisterContextHelp(ContextHelpForm host)
    {
    }

    // --- Event handlers ---

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
        var fileName = SaveFilePathSelector(
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
        var fileName = OpenFilePathSelector(
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

        var fileName = SaveFilePathSelector(
            "JSON files (*.json)|*.json|All files (*.*)|*.*",
            "Export Config");
        if (string.IsNullOrEmpty(fileName))
            return;

        try
        {
            var database = GetDatabase();
            var exportConfig = _appConfigService.GetConfigForExport(item.Path, database);
            string json;
            if (item.Path == null)
            {
                var mainDb = _appFilter.FilterForMainConfig(database);
                mainDb.Apps = exportConfig.Apps;
                mainDb.Settings.HandlerMappings = exportConfig.HandlerMappings != null
                    ? new Dictionary<string, HandlerMappingEntry>(exportConfig.HandlerMappings, StringComparer.OrdinalIgnoreCase)
                    : null;
                var exportDb = mainDb;
                json = JsonSerializer.Serialize(exportDb, JsonDefaults.Options);
            }
            else
            {
                json = JsonSerializer.Serialize(exportConfig, JsonDefaults.Options);
            }

            File.WriteAllText(fileName, json);
            _messageBoxService.Show($"Config exported to {Path.GetFileName(fileName)}.", "Export Complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _log.Error("Config export failed", ex);
            _messageBoxService.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnImportConfigClick(object? sender, EventArgs e)
    {
        if (_configListBox.SelectedItem is not ConfigComboItem item)
            return;

        var importPath = OpenFilePathSelector(
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
                var session = _sessionProvider.GetSession();
                var sidResolutions = await _sidResolutionService.ResolveSidsAsync(
                    session.CredentialStore, session.Database.SidNames);
                var result = _importHandler.ImportMainConfig(importPath, sidResolutions);
                var postImportWarnings = new List<string>();
                DataChanged?.Invoke();

                try
                {
                    _handlerSyncHelper.Sync();
                }
                catch (Exception ex)
                {
                    _log.Error("Main config import handler sync failed", ex);
                    postImportWarnings.Add($"Handler sync failed: {ex.Message}");
                }

                RefreshConfigList();
                var warnings = result.Warnings.Concat(postImportWarnings).ToList();
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
                var result = _additionalImportCoordinator.ImportAdditionalConfig(importPath, item.Path);
                if (result.Status == AdditionalConfigImportStatus.Succeeded)
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
                    throw new InvalidOperationException(
                        $"Additional config import failed ({result.Status}): {errorText}");
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

    private static string? SelectSavePath(string filter, string title)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = filter,
            Title = title
        };
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }

    private static string? SelectOpenPath(string filter, string title)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = filter,
            Title = title
        };
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }
}
