using System.ComponentModel;
using System.Text.Json;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.UI;

namespace RunFence.Persistence.UI.Forms;

/// <summary>
/// Config file management section: create, load, unload, export, and import app configs.
/// Used by OptionsPanel.
/// </summary>
internal partial class ConfigManagerSection : UserControl
{
    private readonly IAppConfigService _appConfigService;
    private readonly IAppFilter _appFilter;
    private readonly ILoggingService _log;
    private readonly IAclPermissionService _aclPermission;
    private readonly ILicenseService _licenseService;
    private readonly HandlerSyncHelper? _handlerSyncHelper;

    // Data access delegates (set after construction, before use)
    private Func<AppDatabase> _getDatabase = null!;
    private Func<CredentialStore> _getCredentialStore = null!;
    private Func<ProtectedBuffer> _getPinDerivedKey = null!;

    /// <summary>Fired when a config file should be loaded by the parent.</summary>
    public event Action<string>? ConfigLoadRequested;

    /// <summary>Fired when a config file should be unloaded by the parent.</summary>
    public event Action<string>? ConfigUnloadRequested;

    /// <summary>Fired when config data changes and the parent should refresh.</summary>
    public event Action? DataChanged;

    internal ConfigManagerSection(IAppConfigService appConfigService,
        IAppFilter appFilter, ILoggingService log, IAclPermissionService aclPermission,
        ILicenseService licenseService, HandlerSyncHelper? handlerSyncHelper = null)
    {
        _appConfigService = appConfigService;
        _appFilter = appFilter;
        _log = log;
        _aclPermission = aclPermission;
        _licenseService = licenseService;
        _handlerSyncHelper = handlerSyncHelper;
        InitializeComponent();
        _configNewButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C4", Color.FromArgb(0x22, 0x8B, 0x22));
        _configLoadButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C2", Color.FromArgb(0xCC, 0x99, 0x00));
        _configUnloadButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4E4", Color.FromArgb(0xCC, 0x66, 0x00));
        _configExportButton.Image = UiIconFactory.CreateToolbarIcon("\u2191", Color.FromArgb(0x33, 0x66, 0x99));
        _configImportButton.Image = UiIconFactory.CreateToolbarIcon("\u21A9", Color.FromArgb(0x66, 0x66, 0x99));
    }

    /// <summary>
    /// Sets data access callbacks. Must be called before any interaction.
    /// </summary>
    public void SetDataContext(Func<AppDatabase> getDb, Func<CredentialStore> getStore, Func<ProtectedBuffer> getKey)
    {
        _getDatabase = getDb;
        _getCredentialStore = getStore;
        _getPinDerivedKey = getKey;
    }

    public void RefreshConfigList()
    {
        _configListBox.Items.Clear();
        _configListBox.Items.Add(new ConfigComboItem(null)); // "Main Config"
        foreach (var path in _appConfigService.GetLoadedConfigPaths())
            _configListBox.Items.Add(new ConfigComboItem(path));
        if (_configListBox.Items.Count > 0)
            _configListBox.SelectedIndex = 0;
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
        using var dlg = new SaveFileDialog();
        dlg.Filter = "RunFence Config (*.rfn)|*.rfn|All files (*.*)|*.*";
        dlg.Title = "Create New App Config";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            using (var scope = _getPinDerivedKey().Unprotect())
                _appConfigService.CreateEmptyConfig(dlg.FileName, scope.Data, _getCredentialStore().ArgonSalt);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to create config", ex);
            MessageBox.Show($"Failed to create config: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (MessageBox.Show(
                "Restrict access to this config file to Administrators only?",
                "Set Permissions", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            try
            {
                _aclPermission.RestrictToAdmins(dlg.FileName);
            }
            catch (Exception ex)
            {
                _log.Error("Failed to restrict config file permissions", ex);
                MessageBox.Show($"Failed to set permissions: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        ConfigLoadRequested?.Invoke(dlg.FileName);
    }

    private void OnLoadConfigClick(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog();
        dlg.Filter = "RunFence Config (*.rfn)|*.rfn|All files (*.*)|*.*";
        dlg.Title = "Load App Config";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog() != DialogResult.OK)
            return;

        ConfigLoadRequested?.Invoke(dlg.FileName);
    }

    private void OnUnloadConfigClick(object? sender, EventArgs e)
    {
        if (_configListBox.SelectedItem is not ConfigComboItem item || item.Path == null)
        {
            MessageBox.Show("Select an additional config to unload.", "No Selection",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ConfigUnloadRequested?.Invoke(item.Path);
    }

    private void OnExportConfigClick(object? sender, EventArgs e)
    {
        if (_configListBox.SelectedItem is not ConfigComboItem item)
            return;

        using var dlg = new SaveFileDialog();
        dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
        dlg.Title = "Export Config";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            var database = _getDatabase();
            string json;
            if (item.Path == null)
            {
                var mainDb = _appFilter.FilterForMainConfig(database);
                var exportDb = new AppDatabase
                {
                    Apps = mainDb.Apps,
                    Settings = database.Settings,
                    Accounts = mainDb.Accounts,
                    SidNames = database.SidNames,
                };
                json = JsonSerializer.Serialize(exportDb, JsonDefaults.Options);
            }
            else
            {
                var apps = _appConfigService.GetAppsForConfig(item.Path, database);
                var exportConfig = new AppConfig { Apps = apps };
                json = JsonSerializer.Serialize(exportConfig, JsonDefaults.Options);
            }

            File.WriteAllText(dlg.FileName, json);
            MessageBox.Show($"Config exported to {Path.GetFileName(dlg.FileName)}.", "Export Complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _log.Error("Config export failed", ex);
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnImportConfigClick(object? sender, EventArgs e)
    {
        if (_configListBox.SelectedItem is not ConfigComboItem item)
            return;

        using var openDlg = new OpenFileDialog();
        openDlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
        openDlg.Title = "Import Config";
        FileDialogHelper.AddInteractiveUserCustomPlaces(openDlg);
        if (openDlg.ShowDialog() != DialogResult.OK)
            return;

        var confirm = MessageBox.Show(
            "This will import plaintext config data and overwrite the current configuration.\n\nContinue?",
            "Confirm Import", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
            return;

        try
        {
            var json = File.ReadAllText(openDlg.FileName);
            var database = _getDatabase();

            if (item.Path == null)
            {
                var importedDb = JsonSerializer.Deserialize<AppDatabase>(json, JsonDefaults.Options)
                                 ?? throw new InvalidOperationException("Failed to parse config file.");

                // Imported configs are trusted — it is the user's responsibility to verify the source.
                // AllowedIpcCallers and other security settings are replaced as-is.
                if (!_licenseService.IsLicensed)
                {
                    var violations = new List<string>();
                    var additionalAppsCount = database.Apps.Count(a => _appConfigService.GetConfigPath(a.Id) != null);
                    var totalAfterImport = importedDb.Apps.Count + additionalAppsCount;
                    var appsMsg = _licenseService.GetRestrictionMessage(EvaluationFeature.Apps, totalAfterImport - 1);
                    if (appsMsg != null)
                        violations.Add($"Apps: {appsMsg}");
                    var totalAllowlistEntries = importedDb.Accounts?.Sum(a => a.Firewall.Allowlist.Count) ?? 0;
                    if (totalAllowlistEntries > Constants.EvaluationMaxFirewallAllowlistEntries)
                        violations.Add(
                            $"Firewall whitelist: imported config has {totalAllowlistEntries} entries across all accounts (limit: {Constants.EvaluationMaxFirewallAllowlistEntries})");
                    if (violations.Count > 0)
                    {
                        MessageBox.Show(
                            "Cannot import: the imported config exceeds evaluation limits.\n\n" +
                            string.Join("\n", violations) + "\n\nActivate a license to remove these limits.",
                            "Evaluation Limit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                var additionalApps = database.Apps
                    .Where(a => _appConfigService.GetConfigPath(a.Id) != null)
                    .ToList();
                database.Apps.Clear();
                database.Apps.AddRange(importedDb.Apps);
                foreach (var app in additionalApps)
                {
                    if (database.Apps.Any(a => a.Id == app.Id))
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            var candidate = AppEntry.GenerateId();
                            if (database.Apps.All(a => a.Id != candidate) && !additionalApps.Any(a => a != app && a.Id == candidate))
                            {
                                app.Id = candidate;
                                break;
                            }
                        }
                    }
                }

                database.Apps.AddRange(additionalApps);

                database.Settings = importedDb.Settings ?? new AppSettings();

                // Merge SidNames additively — the SidNames map is append-only and must never lose
                // associations already known by this installation.
                foreach (var (sid, name) in importedDb.SidNames)
                    database.SidNames.TryAdd(sid, name);

                // Replace IPC callers and firewall settings from imported config.
                // Tray flags, split-token, low-integrity, ephemeral, and grants are per-installation
                // state that are preserved and never overwritten by the imported config.
                foreach (var a in database.Accounts)
                {
                    a.IsIpcCaller = false;
                    a.Firewall = new FirewallAccountSettings();
                }

                foreach (var importedAccount in importedDb.Accounts ?? [])
                {
                    if (importedAccount is { IsIpcCaller: false, Firewall.IsDefault: true })
                        continue;
                    var entry = database.GetOrCreateAccount(importedAccount.Sid);
                    entry.IsIpcCaller = importedAccount.IsIpcCaller;
                    entry.Firewall = importedAccount.Firewall;
                }

                foreach (var a in database.Accounts.ToList())
                    database.RemoveAccountIfEmpty(a.Sid);

                using var scope = _getPinDerivedKey().Unprotect();
                _appConfigService.ReencryptAndSaveAll(_getCredentialStore(), database, scope.Data);

                DataChanged?.Invoke();
                _handlerSyncHelper?.Sync();
                RefreshConfigList();
                MessageBox.Show("Main config imported successfully.", "Import Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                var importedConfig = JsonSerializer.Deserialize<AppConfig>(json, JsonDefaults.Options)
                                     ?? throw new InvalidOperationException("Failed to parse config file.");

                ConfigUnloadRequested?.Invoke(item.Path);

                using (var scope = _getPinDerivedKey().Unprotect())
                    _appConfigService.SaveImportedConfig(item.Path, importedConfig.Apps, scope.Data, _getCredentialStore().ArgonSalt);

                ConfigLoadRequested?.Invoke(item.Path);
                MessageBox.Show("Config imported successfully.", "Import Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Config import failed", ex);
            MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}