using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Owns the mutable state and domain mutation operations for handler mappings:
/// adding, changing, removing app-based and direct handler mappings, and restoring HKCU
/// fallback entries when handlers are removed or their type changes.
/// Each mutation applies <see cref="AppEntry.AllowPassingArguments"/> immediately and fires
/// <see cref="Changed"/> so subscribers (e.g. <see cref="HandlerMappingsDialog"/>) can save.
/// </summary>
public class HandlerMappingMutationHandler(
    IHandlerMappingService handlerMappingService,
    IAssociationAutoSetService autoSetService,
    IDatabaseProvider databaseProvider,
    IHandlerMappingNotifier notifier)
{
    /// <summary>
    /// Raised after each mutation so subscribers (e.g. <see cref="HandlerMappingsDialog"/>)
    /// can react without polling. Fired on the UI thread — no cross-thread marshaling needed.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Adds app-based mappings for each key. Keys are assumed to have been validated by the caller.
    /// If the app does not have AllowPassingArguments enabled, enables it immediately with a user warning.
    /// </summary>
    public void AddAppMapping(IReadOnlyList<string> keys, AppEntry selectedApp, string? template,
        IReadOnlyList<string>? prefixes = null, bool replacePrefixes = false)
    {
        if (!selectedApp.AllowPassingArguments)
        {
            selectedApp.AllowPassingArguments = true;
            notifier.ShowAllowPassingArgumentsEnabled(selectedApp.Name);
        }

        var db = databaseProvider.GetDatabase();
        foreach (var key in keys)
            handlerMappingService.SetHandlerMapping(key,
                new HandlerMappingEntry(selectedApp.Id, template,
                    PathPrefixes: prefixes?.Count > 0 ? [..prefixes] : null,
                    ReplacePrefixes: replacePrefixes),
                db);

        Changed?.Invoke();
    }

    /// <summary>
    /// Adds direct handler mappings for each key using pre-resolved handler entries.
    /// Restores HKCU fallback when the handler type changes for an existing key.
    /// </summary>
    public void AddDirectHandler(IReadOnlyList<string> keys, IReadOnlyList<DirectHandlerEntry> resolvedEntries)
    {
        var db = databaseProvider.GetDatabase();
        var existingDirectMappings = handlerMappingService.GetEffectiveDirectHandlerMappings(db);

        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var newEntry = resolvedEntries[i];

            if (existingDirectMappings.TryGetValue(key, out var currentEntry) &&
                ((currentEntry.ClassName != null && newEntry.ClassName == null) ||
                 (currentEntry.Command != null && newEntry.Command == null)))
                autoSetService.RestoreKeyForAllUsers(key);

            handlerMappingService.SetDirectHandlerMapping(key, newEntry, db);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Changes the app and/or template for an existing app-based mapping.
    /// Returns false and makes no changes when neither the app, the template, nor the prefixes changed.
    /// If the new app does not have AllowPassingArguments enabled, enables it immediately with a user warning.
    /// </summary>
    public bool ChangeAppMapping(string key, string oldAppId, AppEntry newApp, string? newTemplate,
        string? currentTemplateInRow,
        IReadOnlyList<string>? currentPrefixes = null, IReadOnlyList<string>? newPrefixes = null,
        bool currentReplacePrefixes = false, bool newReplacePrefixes = false)
    {
        var existingTemplate = string.IsNullOrEmpty(currentTemplateInRow) ? null : currentTemplateInRow;
        if (string.Equals(newApp.Id, oldAppId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(newTemplate, existingTemplate, StringComparison.Ordinal) &&
            (currentPrefixes ?? []).SequenceEqual(newPrefixes ?? [], StringComparer.OrdinalIgnoreCase) &&
            currentReplacePrefixes == newReplacePrefixes)
            return false;

        var db = databaseProvider.GetDatabase();
        handlerMappingService.RemoveHandlerMapping(key, oldAppId, db);
        if (!newApp.AllowPassingArguments)
        {
            newApp.AllowPassingArguments = true;
            notifier.ShowAllowPassingArgumentsEnabled(newApp.Name);
        }
        handlerMappingService.SetHandlerMapping(key,
            new HandlerMappingEntry(newApp.Id, newTemplate,
                PathPrefixes: newPrefixes?.Count > 0 ? [..newPrefixes] : null,
                ReplacePrefixes: newReplacePrefixes),
            db);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Edits a direct handler mapping with a pre-resolved entry.
    /// Restores HKCU fallback when the handler type changes.
    /// </summary>
    public void EditDirectHandler(string key, DirectHandlerEntry currentEntry, DirectHandlerEntry newEntry)
    {
        if (currentEntry.ClassName != null && newEntry.ClassName == null)
            autoSetService.RestoreKeyForAllUsers(key);
        else if (currentEntry.Command != null && newEntry.Command == null)
            autoSetService.RestoreKeyForAllUsers(key);

        var db = databaseProvider.GetDatabase();
        handlerMappingService.SetDirectHandlerMapping(key, newEntry, db);
        Changed?.Invoke();
    }

    /// <summary>
    /// Removes an app-based mapping. Restores HKCU fallback when the key has no remaining app mappings.
    /// If no handler mappings remain for this app across all keys, disables AllowPassingArguments immediately.
    /// </summary>
    public void RemoveMapping(AppMappingRowTag tag)
    {
        var db = databaseProvider.GetDatabase();
        handlerMappingService.RemoveHandlerMapping(tag.Key, tag.AppId, db);
        var remaining = handlerMappingService.GetAllHandlerMappings(db);
        if (!remaining.ContainsKey(tag.Key))
            autoSetService.RestoreKeyForAllUsers(tag.Key);

        if (!remaining.Values.SelectMany(ids => ids)
                .Any(e => string.Equals(e.AppId, tag.AppId, StringComparison.OrdinalIgnoreCase)))
        {
            var app = db.Apps.FirstOrDefault(a => string.Equals(a.Id, tag.AppId, StringComparison.OrdinalIgnoreCase));
            if (app is { AllowPassingArguments: true })
            {
                app.AllowPassingArguments = false;
                notifier.ShowAllowPassingArgumentsDisabled(app.Name);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Removes a direct handler mapping and restores the HKCU fallback.
    /// </summary>
    public void RemoveDirectHandler(DirectHandlerRowTag tag)
    {
        var db = databaseProvider.GetDatabase();
        handlerMappingService.RemoveDirectHandlerMapping(tag.Key, db);
        autoSetService.RestoreKeyForAllUsers(tag.Key);
        Changed?.Invoke();
    }

    /// <summary>
    /// Applies imported association entries to the database as direct handler mappings.
    /// Entries are assumed to have been validated and selected by the caller.
    /// </summary>
    public void ApplyImportedAssociations(IReadOnlyList<InteractiveAssociationEntry> entries)
    {
        var db = databaseProvider.GetDatabase();
        foreach (var entry in entries)
            handlerMappingService.SetDirectHandlerMapping(entry.Key, entry.Handler, db);

        Changed?.Invoke();
    }

    /// <summary>
    /// Replaces all app-based handler mappings for the given app with the provided associations,
    /// computing a diff and applying only the minimal set of changes. Fires <see cref="Changed"/>
    /// when any additions or removals occurred, allowing subscribers to sync.
    /// Returns the list of keys that were removed.
    /// </summary>
    public IReadOnlyList<string> SetAssociationsForApp(string appId, IReadOnlyList<HandlerAssociationItem> newAssociations)
    {
        var db = databaseProvider.GetDatabase();
        var allMappings = handlerMappingService.GetAllHandlerMappings(db);

        var originalItems = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in allMappings)
        {
            var entry = kv.Value.FirstOrDefault(e => string.Equals(e.AppId, appId, StringComparison.OrdinalIgnoreCase));
            if (entry.AppId != null)
                originalItems[kv.Key] = entry;
        }

        var newKeys = newAssociations
            .Select(a => a.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removedKeys = new List<string>();
        foreach (var key in originalItems.Keys)
        {
            if (!newKeys.Contains(key))
            {
                handlerMappingService.RemoveHandlerMapping(key, appId, db);
                removedKeys.Add(key);
            }
        }

        bool anyMutated = false;
        foreach (var item in newAssociations)
        {
            bool isNew = !originalItems.ContainsKey(item.Key);
            bool templateChanged = !isNew && originalItems[item.Key].ArgumentsTemplate != item.ArgumentsTemplate;
            bool prefixesChanged = !isNew &&
                (!(originalItems[item.Key].PathPrefixes ?? []).SequenceEqual(item.PathPrefixes ?? [], StringComparer.OrdinalIgnoreCase) ||
                 originalItems[item.Key].ReplacePrefixes != item.ReplacePrefixes);

            if (isNew || templateChanged || prefixesChanged)
            {
                handlerMappingService.SetHandlerMapping(item.Key,
                    new HandlerMappingEntry(appId, item.ArgumentsTemplate,
                        PathPrefixes: item.PathPrefixes?.Count > 0 ? [..item.PathPrefixes] : null,
                        ReplacePrefixes: item.ReplacePrefixes),
                    db);
                anyMutated = true;
            }
        }

        if (anyMutated || removedKeys.Count > 0)
            Changed?.Invoke();

        return removedKeys;
    }

    /// <summary>
    /// Updates the app-level path prefix constraint for the specified app entry.
    /// Sets to null when <paramref name="prefixes"/> is null or empty.
    /// </summary>
    public void UpdateAppPrefixes(string appId, IReadOnlyList<string>? prefixes)
    {
        var db = databaseProvider.GetDatabase();
        var app = db.Apps.FirstOrDefault(a => string.Equals(a.Id, appId, StringComparison.OrdinalIgnoreCase));
        if (app == null) return;
        app.PathPrefixes = prefixes?.Count > 0 ? [..prefixes] : null;
        Changed?.Invoke();
    }

}
