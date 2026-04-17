using RunFence.Apps;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Owns the mutable state and domain mutation operations for handler mappings:
/// adding, changing, removing app-based and direct handler mappings, and tracking
/// pending <see cref="AppEntry.AllowPassingArguments"/> changes across operations.
/// </summary>
public class HandlerMappingMutationHandler(
    IHandlerMappingService handlerMappingService)
{
    private readonly Dictionary<string, bool> _pendingAllowPassingArgs = new(StringComparer.OrdinalIgnoreCase);
    private Func<AppDatabase> _getDatabase = null!;
    private bool _hasChanges;

    /// <summary>
    /// Wires the database accessor. Must be called before any mutation operations.
    /// </summary>
    public void Initialize(Func<AppDatabase> getDatabase)
    {
        _getDatabase = getDatabase;
    }

    /// <summary>
    /// Returns true when any mappings have been modified since initialization.
    /// </summary>
    public bool HasChanges => _hasChanges;

    /// <summary>
    /// Adds app-based mappings for each key. Keys are assumed to have been validated by the caller.
    /// Tracks pending AllowPassingArguments changes for apps that need it enabled.
    /// </summary>
    public void AddAppMapping(IReadOnlyList<string> keys, AppEntry selectedApp, string? template)
    {
        if (!selectedApp.AllowPassingArguments)
            _pendingAllowPassingArgs[selectedApp.Id] = true;

        var db = _getDatabase();
        foreach (var key in keys)
            handlerMappingService.SetHandlerMapping(key, new HandlerMappingEntry(selectedApp.Id, template), db);

        _hasChanges = true;
    }

    /// <summary>
    /// Adds direct handler mappings for each key using pre-resolved handler entries.
    /// HKCU restoration is the caller's responsibility when the handler type changes.
    /// </summary>
    public void AddDirectHandler(IReadOnlyList<string> keys, IReadOnlyList<DirectHandlerEntry> resolvedEntries)
    {
        var db = _getDatabase();
        for (var i = 0; i < keys.Count; i++)
            handlerMappingService.SetDirectHandlerMapping(keys[i], resolvedEntries[i], db);

        _hasChanges = true;
    }

    /// <summary>
    /// Changes the app and/or template for an existing app-based mapping.
    /// Returns false and makes no changes when neither the app nor the template changed.
    /// </summary>
    public bool ChangeAppMapping(string key, string oldAppId, AppEntry newApp, string? newTemplate, string? currentTemplateInRow)
    {
        var existingTemplate = string.IsNullOrEmpty(currentTemplateInRow) ? null : currentTemplateInRow;
        if (string.Equals(newApp.Id, oldAppId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(newTemplate, existingTemplate, StringComparison.Ordinal))
            return false;

        var db = _getDatabase();
        handlerMappingService.RemoveHandlerMapping(key, oldAppId, db);
        if (!newApp.AllowPassingArguments)
            _pendingAllowPassingArgs[newApp.Id] = true;
        handlerMappingService.SetHandlerMapping(key, new HandlerMappingEntry(newApp.Id, newTemplate), db);
        _hasChanges = true;
        return true;
    }

    /// <summary>
    /// Edits a direct handler mapping with a pre-resolved entry.
    /// HKCU restoration is the caller's responsibility when the handler type changes.
    /// </summary>
    public void EditDirectHandler(string key, DirectHandlerEntry newEntry)
    {
        var db = _getDatabase();
        handlerMappingService.SetDirectHandlerMapping(key, newEntry, db);
        _hasChanges = true;
    }

    /// <summary>
    /// Removes an app-based mapping. Returns true when the key has no remaining app mappings
    /// (indicating the caller should restore the HKCU fallback for that key).
    /// </summary>
    public bool RemoveMapping(AppMappingRowTag tag)
    {
        var db = _getDatabase();
        handlerMappingService.RemoveHandlerMapping(tag.Key, tag.AppId, db);
        _hasChanges = true;
        var remaining = handlerMappingService.GetAllHandlerMappings(db);
        return !remaining.ContainsKey(tag.Key);
    }

    /// <summary>
    /// Removes a direct handler mapping. The caller is responsible for restoring the HKCU fallback.
    /// </summary>
    public void RemoveDirectHandler(DirectHandlerRowTag tag)
    {
        var db = _getDatabase();
        handlerMappingService.RemoveDirectHandlerMapping(tag.Key, db);
        _hasChanges = true;
    }

    /// <summary>
    /// Applies imported association entries to the database as direct handler mappings.
    /// Entries are assumed to have been validated and selected by the caller.
    /// </summary>
    public void ApplyImportedAssociations(IReadOnlyList<InteractiveAssociationEntry> entries)
    {
        var db = _getDatabase();
        foreach (var entry in entries)
            handlerMappingService.SetDirectHandlerMapping(entry.Key, entry.Handler, db);

        _hasChanges = true;
    }

    /// <summary>
    /// Applies all pending AllowPassingArguments changes to the live app objects.
    /// Must be called before saving the database when <see cref="HasChanges"/> is true.
    /// </summary>
    public void ApplyPendingAllowPassingArgs(AppDatabase db)
    {
        foreach (var (appId, value) in _pendingAllowPassingArgs)
        {
            var app = db.Apps.FirstOrDefault(a => string.Equals(a.Id, appId, StringComparison.OrdinalIgnoreCase));
            if (app != null)
                app.AllowPassingArguments = value;
        }
        _pendingAllowPassingArgs.Clear();
    }
}
