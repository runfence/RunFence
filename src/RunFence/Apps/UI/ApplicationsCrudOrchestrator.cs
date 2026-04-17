using RunFence.Acl;
using RunFence.Apps.Shortcuts;
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
public class ApplicationsCrudOrchestrator(
    Func<AppEditDialog> dialogFactory,
    IAclService aclService,
    IIconService iconService,
    IAppConfigService appConfigService,
    AppEntryEnforcementHelper enforcementHelper,
    IShortcutDiscoveryService shortcutDiscovery,
    AppEntryPermissionPrompter permissionPrompter,
    ILoggingService log,
    ILicenseService licenseService)
{
    private IApplicationsPanelContext _context = null!;

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
                AppEntry selectedApp => appConfigService.GetConfigPath(selectedApp.Id),
                ApplicationsPanel.ConfigGroupHeaderTag header => header.ConfigPath,
                _ => initialConfigPath
            };
        }

        var dlg = dialogFactory();
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
                if (!licenseService.CanAddApp(_context.Database.Apps.Count))
                {
                    MessageBox.Show(licenseService.GetRestrictionMessage(EvaluationFeature.Apps, _context.Database.Apps.Count),
                        "License Limit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                appConfigService.AssignApp(dlg.Result.Id, dlg.SelectedConfigPath);
                try
                {
                    _context.Database.Apps.Add(dlg.Result);
                    var shortcutCache = CreateShortcutCacheIfNeeded(dlg.Result);
                    ApplyChanges(dlg.Result, shortcutCache);
                    _context.SaveAndRefresh(dlg.Result.Id, targetedSave: true);
                    if (permissionPrompter.PromptAndGrant(dlg, dlg.Result))
                        _context.SaveAndRefresh(dlg.Result.Id);
                    if (dlg.LaunchNow)
                        _context.LaunchApp(dlg.Result, null);
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                }
                catch
                {
                    _context.Database.Apps.Remove(dlg.Result);
                    appConfigService.RemoveApp(dlg.Result.Id);
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
        var originalConfigPath = appConfigService.GetConfigPath(app.Id);

        var dlg = dialogFactory();
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
                    var shortcutCache = CreateShortcutCacheIfNeeded(app, dlg.Result);
                    RevertChanges(app, shortcutCache);
                    _context.Database.Apps[index] = dlg.Result;
                    appConfigService.AssignApp(dlg.Result.Id, dlg.SelectedConfigPath);
                    try
                    {
                        ApplyChanges(dlg.Result, shortcutCache);
                        _context.SaveAndRefresh(dlg.Result.Id);
                    }
                    catch
                    {
                        _context.Database.Apps[index] = app;
                        appConfigService.AssignApp(app.Id, originalConfigPath);
                        try
                        {
                            ApplyChanges(app, shortcutCache);
                        }
                        catch (Exception restoreEx)
                        {
                            log.Error($"Failed to restore ACL after edit failure for {app.Name}", restoreEx);
                        }

                        throw;
                    }

                    if (permissionPrompter.PromptAndGrant(dlg, dlg.Result))
                        _context.SaveAndRefresh(dlg.Result.Id);
                }

                if (dlg.LaunchNow)
                    _context.LaunchApp(dlg.Result, null);
                dlg.DialogResult = DialogResult.OK;
                dlg.Close();
            };

            dlg.RemoveRequested += () =>
            {
                var shortcutCache = CreateShortcutCacheIfNeeded(app);
                RevertChanges(app, shortcutCache);
                _context.Database.Apps.Remove(app);
                appConfigService.RemoveApp(app.Id);
                iconService.DeleteIcon(app.Id);
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
            var shortcutCache = CreateShortcutCacheIfNeeded(app);
            RevertChanges(app, shortcutCache);
            _context.Database.Apps.Remove(app);
            appConfigService.RemoveApp(app.Id);
            iconService.DeleteIcon(app.Id);
            _context.SaveAndRefresh(fallbackIndex: selectedIndex);
        }
    }

    private void ApplyChanges(AppEntry app, ShortcutTraversalCache shortcutCache)
    {
        try
        {
            enforcementHelper.ApplyChanges(app, _context.Database.Apps, shortcutCache);
            aclService.RecomputeAllAncestorAcls(_context.Database.Apps);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to apply changes for {app.Name}", ex);
            MessageBox.Show($"Failed to apply ACL/shortcut changes for {app.Name}:\n{ex.Message}",
                "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RevertChanges(AppEntry app, ShortcutTraversalCache shortcutCache)
    {
        try
        {
            enforcementHelper.RevertChanges(app, _context.Database.Apps, shortcutCache);
            var appsAfterRevert = _context.Database.Apps.Where(a => a.Id != app.Id).ToList();
            aclService.RecomputeAllAncestorAcls(appsAfterRevert);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to revert changes for {app.Name}", ex);
            MessageBox.Show($"Failed to revert ACL/shortcut changes for {app.Name}:\n{ex.Message}",
                "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private ShortcutTraversalCache CreateShortcutCacheIfNeeded(params AppEntry[] apps)
        => apps.Any(a => a.ManageShortcuts)
            ? shortcutDiscovery.CreateTraversalCache()
            : new ShortcutTraversalCache([]);
}
