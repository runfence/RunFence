using RunFence.Acl;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.RunAs.UI;

namespace RunFence.Apps.UI;

/// <summary>
/// Provides access to shared state and operations for the applications panel CRUD handler.
/// </summary>
public interface IApplicationsPanelContext
{
    AppDatabase Database { get; }
    CredentialStore CredentialStore { get; }
    DataGridView Grid { get; }
    void ShowModalDialog(Form dialog);
    void SaveAndRefresh(string? selectAppId = null, int fallbackIndex = -1, bool targetedSave = false);
    void LaunchApp(AppEntry app, string? launcherArguments);
}

/// <summary>
/// Handles add, edit, and remove operations for app entries in the ApplicationsPanel,
/// including dialog orchestration, enforcement (ACL/shortcuts), and save/refresh.
/// </summary>
public class ApplicationsCrudOrchestrator
{
    private readonly Func<AppEditDialog> _dialogFactory;
    private readonly IAclService _aclService;
    private readonly IIconService _iconService;
    private readonly IAppConfigService _appConfigService;
    private readonly AppEntryEnforcementHelper _enforcementHelper;
    private readonly AppEntryPermissionPrompter _permissionPrompter;
    private readonly ILoggingService _log;
    private readonly ILicenseService _licenseService;
    private IApplicationsPanelContext _context = null!;

    public ApplicationsCrudOrchestrator(
        Func<AppEditDialog> dialogFactory,
        IAclService aclService,
        IIconService iconService,
        IAppConfigService appConfigService,
        AppEntryEnforcementHelper enforcementHelper,
        AppEntryPermissionPrompter permissionPrompter,
        ILoggingService log,
        ILicenseService licenseService)
    {
        _dialogFactory = dialogFactory;
        _aclService = aclService;
        _iconService = iconService;
        _appConfigService = appConfigService;
        _enforcementHelper = enforcementHelper;
        _permissionPrompter = permissionPrompter;
        _log = log;
        _licenseService = licenseService;
    }

    public void Initialize(IApplicationsPanelContext context)
    {
        _context = context;
    }

    public void OpenAddDialogBatch(string[] paths)
    {
        foreach (var path in paths)
            OpenAddDialog(initialExePath: path);
    }

    public void OpenAddDialog(string? initialAccountSid = null, string? initialExePath = null)
    {
        string? initialConfigPath = null;
        var grid = _context.Grid;
        if (grid.SelectedRows.Count > 0)
        {
            initialConfigPath = grid.SelectedRows[0].Tag switch
            {
                AppEntry selectedApp => _appConfigService.GetConfigPath(selectedApp.Id),
                ApplicationsPanel.ConfigGroupHeaderTag header => header.ConfigPath,
                _ => initialConfigPath
            };
        }

        var dlg = _dialogFactory();
        dlg.Initialize(
            null,
            _context.CredentialStore.Credentials,
            _context.Database.Apps,
            new AppEditDialogOptions(ConfigPath: initialConfigPath, AccountSid: initialAccountSid,
                ExePath: initialExePath),
            _context.Database.SidNames,
            _context.Database);

        using (dlg)
        {
            dlg.ApplyRequested += () =>
            {
                if (!_licenseService.CanAddApp(_context.Database.Apps.Count))
                {
                    MessageBox.Show(_licenseService.GetRestrictionMessage(EvaluationFeature.Apps, _context.Database.Apps.Count),
                        "License Limit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _appConfigService.AssignApp(dlg.Result.Id, dlg.SelectedConfigPath);
                try
                {
                    _context.Database.Apps.Add(dlg.Result);
                    ApplyChanges(dlg.Result);
                    _context.SaveAndRefresh(dlg.Result.Id, targetedSave: true);
                    if (_permissionPrompter.PromptAndGrant(dlg, dlg.Result))
                        _context.SaveAndRefresh(dlg.Result.Id);
                    if (dlg.LaunchNow)
                        _context.LaunchApp(dlg.Result, null);
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                }
                catch
                {
                    _context.Database.Apps.Remove(dlg.Result);
                    _appConfigService.RemoveApp(dlg.Result.Id);
                    throw;
                }
            };

            _context.ShowModalDialog(dlg);
        }
    }

    /// <summary>Opens the edit dialog for the currently selected app entry.</summary>
    public void EditSelected()
    {
        var grid = _context.Grid;
        if (grid.SelectedRows.Count == 0)
            return;
        if (grid.SelectedRows[0].Tag is not AppEntry app)
            return;
        OpenEditDialog(app, grid.SelectedRows[0].Index);
    }

    public void EditApp(AppEntry app, AppEditDialogOptions? options = null)
    {
        var selectedIndex = _context.Grid.Rows.Cast<DataGridViewRow>()
            .FirstOrDefault(r => r.Tag is AppEntry a && a.Id == app.Id)?.Index ?? -1;
        OpenEditDialog(app, selectedIndex, options);
    }

    private void OpenEditDialog(AppEntry app, int selectedIndex, AppEditDialogOptions? options = null)
    {
        var originalConfigPath = _appConfigService.GetConfigPath(app.Id);

        var dlg = _dialogFactory();
        dlg.Initialize(
            app,
            _context.CredentialStore.Credentials,
            _context.Database.Apps,
            options,
            _context.Database.SidNames,
            _context.Database);

        using (dlg)
        {
            dlg.ApplyRequested += () =>
            {
                var index = _context.Database.Apps.FindIndex(a => a.Id == app.Id);
                if (index >= 0)
                {
                    RevertChanges(app);
                    _context.Database.Apps[index] = dlg.Result;
                    _appConfigService.AssignApp(dlg.Result.Id, dlg.SelectedConfigPath);
                    try
                    {
                        ApplyChanges(dlg.Result);
                        _context.SaveAndRefresh(dlg.Result.Id);
                    }
                    catch
                    {
                        _context.Database.Apps[index] = app;
                        _appConfigService.AssignApp(app.Id, originalConfigPath);
                        try
                        {
                            ApplyChanges(app);
                        }
                        catch (Exception restoreEx)
                        {
                            _log.Error($"Failed to restore ACL after edit failure for {app.Name}", restoreEx);
                        }

                        throw;
                    }

                    if (_permissionPrompter.PromptAndGrant(dlg, dlg.Result))
                        _context.SaveAndRefresh(dlg.Result.Id);
                }

                if (dlg.LaunchNow)
                    _context.LaunchApp(dlg.Result, null);
                dlg.DialogResult = DialogResult.OK;
                dlg.Close();
            };

            dlg.RemoveRequested += () =>
            {
                RevertChanges(app);
                _context.Database.Apps.Remove(app);
                _appConfigService.RemoveApp(app.Id);
                _iconService.DeleteIcon(app.Id);
                _context.SaveAndRefresh(fallbackIndex: selectedIndex);
                dlg.DialogResult = DialogResult.OK;
                dlg.Close();
            };

            _context.ShowModalDialog(dlg);
        }
    }

    /// <summary>Removes the currently selected app entry after confirmation.</summary>
    public void RemoveSelected()
    {
        var grid = _context.Grid;
        if (grid.SelectedRows.Count == 0)
            return;
        if (grid.SelectedRows[0].Tag is not AppEntry app)
            return;
        var selectedIndex = grid.SelectedRows[0].Index;

        var removeMessage = AppEntryHelper.GetRemoveConfirmationMessage(app);

        if (MessageBox.Show(removeMessage, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            RevertChanges(app);
            _context.Database.Apps.Remove(app);
            _appConfigService.RemoveApp(app.Id);
            _iconService.DeleteIcon(app.Id);
            _context.SaveAndRefresh(fallbackIndex: selectedIndex);
        }
    }

    private void ApplyChanges(AppEntry app)
    {
        try
        {
            _enforcementHelper.ApplyChanges(app, _context.Database.Apps, _context.Database.SidNames);
            _aclService.RecomputeAllAncestorAcls(_context.Database.Apps);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to apply changes for {app.Name}", ex);
            MessageBox.Show($"Failed to apply ACL/shortcut changes for {app.Name}:\n{ex.Message}",
                "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RevertChanges(AppEntry app)
    {
        try
        {
            _enforcementHelper.RevertChanges(app, _context.Database.Apps);
            var appsAfterRevert = _context.Database.Apps.Where(a => a.Id != app.Id).ToList();
            _aclService.RecomputeAllAncestorAcls(appsAfterRevert);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to revert changes for {app.Name}", ex);
            MessageBox.Show($"Failed to revert ACL/shortcut changes for {app.Name}:\n{ex.Message}",
                "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}