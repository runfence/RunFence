using RunFence.Account;
using RunFence.Apps;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Owns handler mapping domain logic for <see cref="Forms.HandlerMappingsDialog"/>:
/// data preparation, mapping mutations, HKCU restoration, HKLM sync orchestration, and display formatting.
/// Decouples all domain operations from the dialog's grid and UI concerns.
/// </summary>
public class HandlerMappingsController(
    IHandlerMappingService handlerMappingService,
    HandlerMappingGridBuilder gridBuilder,
    HandlerMappingMutationHandler mutationHandler,
    HandlerMappingSyncService syncService,
    DirectHandlerResolver directHandlerResolver,
    IAssociationAutoSetService autoSetService)
{
    private Func<AppDatabase> _getDatabase = null!;
    private HashSet<string> _originalRunFenceKeys = null!;

    /// <summary>
    /// Initializes per-use controller data. Must be called before any operations.
    /// </summary>
    public void Initialize(Func<AppDatabase> getDatabase)
    {
        _getDatabase = getDatabase;
        mutationHandler.Initialize(getDatabase);
        _originalRunFenceKeys = new HashSet<string>(
            handlerMappingService.GetAllHandlerMappings(getDatabase()).Keys,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when any mappings have been modified since initialization.
    /// </summary>
    public bool HasChanges => mutationHandler.HasChanges;

    /// <summary>
    /// Builds the list of row data items to populate the grid.
    /// The dialog iterates this list and adds rows with <see cref="HandlerMappingRowData.Tag"/> as Row.Tag.
    /// </summary>
    public IReadOnlyList<HandlerMappingRowData> GetGridRows() =>
        gridBuilder.GetGridRows(_getDatabase());

    /// <summary>
    /// Adds app-based mappings for each key. Keys are assumed to have been validated by the caller.
    /// Tracks pending AllowPassingArguments changes for apps that need it enabled.
    /// </summary>
    public void AddAppMapping(IReadOnlyList<string> keys, AppEntry selectedApp, string? template)
    {
        mutationHandler.AddAppMapping(keys, selectedApp, template);
        syncService.Sync(_getDatabase());
    }

    /// <summary>
    /// Adds direct handler mappings for each key. Keys are assumed to have been validated by the caller.
    /// Restores old HKCU state when overwriting an existing handler of a different type.
    /// </summary>
    public void AddDirectHandler(IReadOnlyList<string> keys, string handlerValue)
    {
        if (string.IsNullOrEmpty(handlerValue))
            return;

        var db = _getDatabase();
        var existingDirectMappings = handlerMappingService.GetEffectiveDirectHandlerMappings(db);
        var resolvedEntries = new List<DirectHandlerEntry>(keys.Count);

        foreach (var key in keys)
        {
            var newEntry = directHandlerResolver.ResolveDirectHandlerEntry(key, handlerValue);

            if (existingDirectMappings.TryGetValue(key, out var currentEntry) &&
                ((currentEntry.ClassName != null && newEntry.ClassName == null) ||
                 (currentEntry.Command != null && newEntry.Command == null)))
                autoSetService.RestoreKeyForAllUsers(key);

            resolvedEntries.Add(newEntry);
        }

        mutationHandler.AddDirectHandler(keys, resolvedEntries);
        syncService.Sync(_getDatabase());
    }

    /// <summary>
    /// Changes the app and/or template for an existing app-based mapping.
    /// Returns false and makes no changes when neither the app nor the template changed.
    /// </summary>
    public bool ChangeAppMapping(string key, string oldAppId, AppEntry newApp, string? newTemplate, string? currentTemplateInRow)
    {
        if (!mutationHandler.ChangeAppMapping(key, oldAppId, newApp, newTemplate, currentTemplateInRow))
            return false;
        syncService.Sync(_getDatabase());
        return true;
    }

    /// <summary>
    /// Edits a direct handler mapping. Restores old HKCU state when the handler type changes.
    /// </summary>
    public void EditDirectHandler(string key, DirectHandlerEntry currentEntry, string newValue)
    {
        var newEntry = directHandlerResolver.ResolveDirectHandlerEntry(key, newValue);

        if (currentEntry.ClassName != null && newEntry.ClassName == null)
            autoSetService.RestoreKeyForAllUsers(key);
        else if (currentEntry.Command != null && newEntry.Command == null)
            autoSetService.RestoreKeyForAllUsers(key);

        mutationHandler.EditDirectHandler(key, newEntry);
        syncService.Sync(_getDatabase());
    }

    /// <summary>
    /// Removes a mapping (either app-based or direct handler).
    /// Restores HKCU fallback when the last app mapping for a key is removed.
    /// </summary>
    public void RemoveMapping(AppMappingRowTag tag)
    {
        bool wasLastMapping = mutationHandler.RemoveMapping(tag);
        if (wasLastMapping)
            autoSetService.RestoreKeyForAllUsers(tag.Key);
        syncService.Sync(_getDatabase());
    }

    /// <summary>
    /// Removes a direct handler mapping and restores the HKCU fallback.
    /// </summary>
    public void RemoveDirectHandler(DirectHandlerRowTag tag)
    {
        mutationHandler.RemoveDirectHandler(tag);
        autoSetService.RestoreKeyForAllUsers(tag.Key);
        syncService.Sync(_getDatabase());
    }

    /// <summary>
    /// Returns the set of existing handler keys for import pre-filtering.
    /// </summary>
    public IReadOnlySet<string> GetExistingKeys()
    {
        var db = _getDatabase();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in handlerMappingService.GetAllHandlerMappings(db).Keys)
            keys.Add(key);
        foreach (var key in handlerMappingService.GetEffectiveDirectHandlerMappings(db).Keys)
            keys.Add(key);
        return keys;
    }

    /// <summary>
    /// Applies imported association entries to the database as direct handler mappings.
    /// Entries are assumed to have been validated and selected by the caller.
    /// </summary>
    public void ApplyImportedAssociations(IReadOnlyList<InteractiveAssociationEntry> entries)
    {
        mutationHandler.ApplyImportedAssociations(entries);
        syncService.Sync(_getDatabase());
    }

    /// <summary>
    /// Applies all pending AllowPassingArguments changes to the live app objects.
    /// Must be called before saving the database when <see cref="HasChanges"/> is true.
    /// </summary>
    public void ApplyPendingAllowPassingArgs(AppDatabase db) =>
        mutationHandler.ApplyPendingAllowPassingArgs(db);

    /// <summary>
    /// Returns true when any handler keys have been newly added since initialization
    /// (used to prompt the user to open Default Apps).
    /// </summary>
    public bool HasNewCapability()
    {
        var currentKeys = handlerMappingService.GetAllHandlerMappings(_getDatabase()).Keys;
        return currentKeys.Any(k => !_originalRunFenceKeys.Contains(k));
    }

    /// <summary>
    /// Returns the effective direct handler entry for a key from the current database.
    /// Returns null when the key has no direct handler.
    /// </summary>
    public DirectHandlerEntry? GetDirectHandlerEntry(string key)
    {
        var currentMappings = handlerMappingService.GetEffectiveDirectHandlerMappings(_getDatabase());
        return currentMappings.TryGetValue(key, out var entry) ? entry : null;
    }

    /// <summary>
    /// Syncs HKLM handler registrations and HKCU auto-set overrides to match the current database state.
    /// </summary>
    public void Sync() => syncService.Sync(_getDatabase());

    /// <summary>
    /// Reads interactive user associations from the registry.
    /// </summary>
    public IReadOnlyList<InteractiveAssociationEntry> GetInteractiveUserAssociations() =>
        directHandlerResolver.GetInteractiveUserAssociations();

}

/// <summary>
/// Row data item for populating the handler mappings grid.
/// </summary>
public record HandlerMappingRowData(
    string Key,
    string HandlerDisplay,
    string AccountDisplay,
    string ArgsTemplate,
    HandlerMappingTag Tag);

/// <summary>
/// Base type for handler mapping row tags, used to identify the mapping in grid rows.
/// </summary>
public abstract record HandlerMappingTag;

/// <summary>
/// Grid row tag identifying an app-based mapping row by its association key and app ID.
/// </summary>
public record AppMappingRowTag(string Key, string AppId) : HandlerMappingTag;

/// <summary>
/// Grid row tag identifying a direct handler mapping row by its association key.
/// </summary>
public record DirectHandlerRowTag(string Key) : HandlerMappingTag;
