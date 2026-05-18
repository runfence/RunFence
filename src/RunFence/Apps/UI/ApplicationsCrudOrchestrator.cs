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
public interface IApplicationsPanelContext : IApplicationMutationContext
{
    CredentialStore CredentialStore { get; }
    DataGridView Grid { get; }
    void ShowModalDialog(Form dialog);
    void LaunchApp(AppEntry app, string? launcherArguments);
}

/// <summary>
/// Handles add, edit, and remove operations for app entries in the ApplicationsPanel,
/// including dialog orchestration, enforcement (ACL/shortcuts), and save/refresh.
/// </summary>
public class ApplicationsCrudOrchestrator(
    Func<AppEditDialog> dialogFactory,
    IIconService iconService,
    IAppConfigService appConfigService,
    ApplicationsCrudOperationService operationService,
    IShortcutDiscoveryService shortcutDiscovery,
    AppEntryPermissionPrompter permissionPrompter,
    IMessageBoxService messageBoxService,
    ILicenseService licenseService)
{
    private IApplicationsPanelContext _context = null!;
    private Task<ShortcutTraversalCache>? _scanTask;

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
        var commandContext = new AppEditDialogCommandContext(
            ApplyAsync: async () =>
            {
                if (!licenseService.CanAddApp(_context.Database.Apps.Count))
                    throw new InvalidOperationException(
                        licenseService.GetRestrictionMessage(EvaluationFeature.Apps, _context.Database.Apps.Count)
                        ?? "Cannot add application.");

                var permissionDecision = permissionPrompter.PromptForGrant(dlg, dlg.Result);
                if (permissionDecision.Result == AppEntryPermissionPromptResult.Canceled)
                    throw new OperationCanceledException();

                if (_context.Database.Apps.Any(a => string.Equals(a.Id, dlg.Result.Id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Application ID '{dlg.Result.Id}' already exists.");

                var appWasAdded = false;
                if (_context.Database.Apps.All(a => !ReferenceEquals(a, dlg.Result)))
                {
                    _context.Database.Apps.Add(dlg.Result);
                    appWasAdded = true;
                }

                var shortcutCache = await CreateShortcutCacheIfNeeded(dlg.Result);
                var operationResult = operationService.ApplyChanges(
                    _context,
                    dlg.Result,
                    shortcutCache,
                    selectAppId: dlg.Result.Id,
                    targetedSave: true);
                if (!HandleOperationResult(operationResult, "save"))
                {
                    if (appWasAdded)
                        RemoveAppById(dlg.Result.Id);
                    throw new InvalidOperationException(operationResult.ErrorMessage ?? "Failed to save application.");
                }

                if (permissionDecision.GrantRequest != null)
                {
                    var grantWarning = permissionPrompter.TryApplyGrant(permissionDecision.GrantRequest);
                    if (!string.IsNullOrWhiteSpace(grantWarning))
                    {
                        messageBoxService.Show(
                            $"Application was saved, but applying the selected permission grant failed:\n\n{grantWarning}",
                            "Saved With Warning",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
                if (dlg.LaunchNow)
                    _context.LaunchApp(dlg.Result, null);
            });
        dlg.Initialize(
            null,
            _context.CredentialStore.Credentials,
            _context.Database.Apps,
            commandContext,
            new AppEditDialogOptions(ConfigPath: initialConfigPath, AccountSid: initialAccountSid,
                ExePath: initialExePath),
            _context.Database.SidNames,
            _context.Database);

        using (dlg)
        {
            _context.ShowModalDialog(dlg);
            if (dlg.HasUnsavedInMemoryMutations)
                _context.RefreshAfterInMemoryMutation(dlg.Result.Id);
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
        var dlg = dialogFactory();
        var commandContext = new AppEditDialogCommandContext(
            ApplyAsync: async () =>
            {
                var index = _context.Database.Apps.FindIndex(a => a.Id == app.Id);
                if (index < 0)
                    throw new InvalidOperationException("The application no longer exists.");

                var previousApp = app.Clone();
                var permissionDecision = permissionPrompter.PromptForGrant(dlg, dlg.Result);
                if (permissionDecision.Result == AppEntryPermissionPromptResult.Canceled)
                    throw new OperationCanceledException();

                var shortcutCache = await CreateShortcutCacheIfNeeded(app, dlg.Result);
                var revertResult = operationService.RevertChanges(
                    _context,
                    app,
                    shortcutCache,
                    ShortcutWarningPolicy.DemoteToWarning);
                string? revertWarning = null;
                if (revertResult.Status == ApplicationsCrudOperationStatus.EnforcementFailed)
                {
                    revertWarning = revertResult.ErrorMessage;
                }
                else if (revertResult.Status == ApplicationsCrudOperationStatus.SucceededWithEnforcementWarning)
                {
                    revertWarning = revertResult.WarningMessage;
                }

                _context.Database.Apps[index] = dlg.Result;
                var applyResult = operationService.ApplyChanges(
                    _context,
                    dlg.Result,
                    shortcutCache,
                    selectAppId: dlg.Result.Id);
                if (applyResult.Status == ApplicationsCrudOperationStatus.SaveFailed)
                {
                    var appsAfterRollback = new List<AppEntry>(_context.Database.Apps);
                    appsAfterRollback[index] = previousApp;
                    var restoreResult = operationService.RestoreEnforcementAfterFailedEdit(
                        previousApp,
                        appsAfterRollback,
                        shortcutCache);
                    // Restore the persisted database entry only after the system state has been rolled back.
                    // The dialog remains open with the user's current edits intact so they can retry
                    // without losing the in-memory UI state.
                    _context.Database.Apps[index] = previousApp;
                    var combinedMessage = CombineFailureMessages(
                        revertWarning,
                        applyResult.ErrorMessage,
                        restoreResult.WarningMessage);
                    HandleOperationResult(
                        new ApplicationsCrudOperationResult(ApplicationsCrudOperationStatus.SaveFailed, ErrorMessage: combinedMessage),
                        "save");
                    throw new InvalidOperationException(combinedMessage);
                }

                var combinedWarnings = new[] { revertWarning, applyResult.WarningMessage }
                    .Where(w => !string.IsNullOrWhiteSpace(w))
                    .ToList();
                var resultToHandle = combinedWarnings.Count == 0
                    ? applyResult
                    : new ApplicationsCrudOperationResult(
                        ApplicationsCrudOperationStatus.SucceededWithEnforcementWarning,
                        WarningMessage: string.Join("\n\n", combinedWarnings));
                HandleOperationResult(resultToHandle, "save");

                if (permissionDecision.GrantRequest != null)
                {
                    var grantWarning = permissionPrompter.TryApplyGrant(permissionDecision.GrantRequest);
                    if (!string.IsNullOrWhiteSpace(grantWarning))
                    {
                        messageBoxService.Show(
                            $"Application was saved, but applying the selected permission grant failed:\n\n{grantWarning}",
                            "Saved With Warning",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }

                if (dlg.LaunchNow)
                    _context.LaunchApp(dlg.Result, null);
            },
            RemoveAsync: async () =>
            {
                var shortcutCache = await CreateShortcutCacheIfNeeded(app);
                var revertResult = operationService.RevertChanges(
                    _context,
                    app,
                    shortcutCache,
                    ShortcutWarningPolicy.TreatAsFailure);
                if (revertResult.Status == ApplicationsCrudOperationStatus.EnforcementFailed)
                {
                    HandleOperationResult(revertResult, "remove");
                    return;
                }

                _context.Database.Apps.Remove(app);
                appConfigService.RemoveApp(app.Id);
                iconService.DeleteIcon(app.Id);
                var saveResult = operationService.SaveAfterMutation(
                    _context,
                    app,
                    fallbackIndex: selectedIndex);
                if (saveResult.Status == ApplicationsCrudOperationStatus.SaveFailed)
                    _context.RefreshAfterInMemoryMutation(fallbackIndex: selectedIndex);
                HandleOperationResult(saveResult, "remove");
                if (saveResult.Status == ApplicationsCrudOperationStatus.Succeeded)
                    dlg.DialogResult = DialogResult.OK;
                dlg.Close();
            });
        dlg.Initialize(
            app,
            _context.CredentialStore.Credentials,
            _context.Database.Apps,
            commandContext,
            options,
            _context.Database.SidNames,
            _context.Database);

        using (dlg)
        {
            _context.ShowModalDialog(dlg);
            if (dlg.HasUnsavedInMemoryMutations)
                _context.RefreshAfterInMemoryMutation(dlg.Result.Id, selectedIndex);
        }
    }

    /// <summary>Removes the currently selected app entry after confirmation.</summary>
    public async Task RemoveSelected()
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
            var shortcutCache = await CreateShortcutCacheIfNeeded(app);
            var revertResult = operationService.RevertChanges(
                _context,
                app,
                shortcutCache,
                ShortcutWarningPolicy.TreatAsFailure);
            if (revertResult.Status == ApplicationsCrudOperationStatus.EnforcementFailed)
            {
                HandleOperationResult(revertResult, "remove");
                return;
            }

            _context.Database.Apps.Remove(app);
            appConfigService.RemoveApp(app.Id);
            iconService.DeleteIcon(app.Id);
            var saveResult = operationService.SaveAfterMutation(
                _context,
                app,
                fallbackIndex: selectedIndex);
            if (saveResult.Status == ApplicationsCrudOperationStatus.SaveFailed)
                _context.RefreshAfterInMemoryMutation(fallbackIndex: selectedIndex);
            HandleOperationResult(saveResult, "remove");
        }
    }

    private bool HandleOperationResult(ApplicationsCrudOperationResult result, string actionVerb)
    {
        switch (result.Status)
        {
            case ApplicationsCrudOperationStatus.Succeeded:
                return true;
            case ApplicationsCrudOperationStatus.SaveFailed:
                messageBoxService.Show(
                    $"Application {actionVerb} failed because RunFence could not save the change:\n\n{result.ErrorMessage}",
                    "Save Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            case ApplicationsCrudOperationStatus.SucceededWithEnforcementWarning:
                messageBoxService.Show(
                    $"Application was saved, but enforcement failed and needs retry:\n\n{result.WarningMessage}",
                    "Saved With Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return true;
            case ApplicationsCrudOperationStatus.EnforcementFailed:
                messageBoxService.Show(
                    $"Application {actionVerb} failed during cleanup/enforcement:\n\n{result.ErrorMessage}",
                    "Enforcement Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            default:
                return false;
        }
    }

    private static string? CombineFailureMessages(string? firstMessage, string? secondMessage, string? thirdMessage = null)
    {
        var messages = new[] { firstMessage, secondMessage, thirdMessage }
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .ToList();

        return messages.Count switch
        {
            0 => null,
            1 => messages[0],
            _ => string.Join("\n\n", messages)
        };
    }

    private void RemoveAppById(string appId)
    {
        var index = _context.Database.Apps.FindIndex(a => string.Equals(a.Id, appId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            _context.Database.Apps.RemoveAt(index);
    }

    /// <remarks>
    /// Concurrent calls share a single in-progress scan - if a scan is already running, awaits the
    /// existing task rather than starting a second scan. The field is cleared after the task completes
    /// so subsequent calls start a fresh scan. Managed SIDs are captured on the UI thread before the
    /// background scan starts to avoid accessing the live database from the thread pool.
    /// </remarks>
    private async Task<ShortcutTraversalCache> CreateShortcutCacheIfNeeded(params AppEntry[] apps)
    {
        if (!apps.Any(a => a.ManageShortcuts))
            return new ShortcutTraversalCache([]);

        if (_scanTask == null)
        {
            var managedSids = shortcutDiscovery.CaptureManagedSids();
            _scanTask = Task.Run(() => shortcutDiscovery.CreateTraversalCache(managedSids));
        }

        try
        {
            return await _scanTask;
        }
        finally
        {
            _scanTask = null;
        }
    }
}
