using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Owns in-memory handler mapping mutations for add/edit/remove flows.
/// Registry synchronization and HKCU restore side effects are handled separately by callers.
/// </summary>
public class HandlerMappingMutationHandler(IHandlerMappingService handlerMappingService)
{
    public event Action? Changed;

    public void AddAppMapping(
        AppDatabase database,
        IReadOnlyList<string> keys,
        AppEntry selectedApp,
        string? template,
        IReadOnlyList<string>? prefixes = null,
        bool replacePrefixes = false,
        bool requiresAllowPassingArgumentsEnable = false)
    {
        if (requiresAllowPassingArgumentsEnable && !selectedApp.AllowPassingArguments)
            selectedApp.AllowPassingArguments = true;

        foreach (var key in keys)
        {
            handlerMappingService.SetHandlerMapping(
                key,
                new HandlerMappingEntry(
                    selectedApp.Id,
                    template,
                    PathPrefixes: prefixes?.Count > 0 ? [..prefixes] : null,
                    ReplacePrefixes: replacePrefixes),
                database);
        }

        Changed?.Invoke();
    }

    public HandlerMappingMutationOutcome AddDirectHandler(
        AppDatabase database,
        IReadOnlyList<string> keys,
        IReadOnlyList<DirectHandlerEntry> resolvedEntries)
    {
        var existingDirectMappings = handlerMappingService.GetEffectiveDirectHandlerMappings(database);
        var keysToRestore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var newEntry = resolvedEntries[i];

            if (existingDirectMappings.TryGetValue(key, out var currentEntry) &&
                ((currentEntry.ClassName != null && newEntry.Command != null) ||
                 (currentEntry.Command != null && newEntry.ClassName != null)))
            {
                keysToRestore.Add(key);
            }

            handlerMappingService.SetDirectHandlerMapping(key, newEntry, database);
        }

        Changed?.Invoke();
        return new HandlerMappingMutationOutcome([..keysToRestore]);
    }

    public bool ChangeAppMapping(
        AppDatabase database,
        string key,
        string oldAppId,
        AppEntry newApp,
        string? newTemplate,
        string? currentTemplateInRow,
        IReadOnlyList<string>? currentPrefixes = null,
        IReadOnlyList<string>? newPrefixes = null,
        bool currentReplacePrefixes = false,
        bool newReplacePrefixes = false,
        bool requiresAllowPassingArgumentsEnable = false)
    {
        var existingTemplate = string.IsNullOrEmpty(currentTemplateInRow) ? null : currentTemplateInRow;
        var templateChanged = !string.Equals(newTemplate, existingTemplate, StringComparison.Ordinal);
        var prefixesChanged = !(currentPrefixes ?? []).SequenceEqual(newPrefixes ?? [], StringComparer.OrdinalIgnoreCase);
        var replaceModeChanged = currentReplacePrefixes != newReplacePrefixes;
        var appWillMutate = requiresAllowPassingArgumentsEnable && !newApp.AllowPassingArguments;

        if (string.Equals(newApp.Id, oldAppId, StringComparison.OrdinalIgnoreCase) &&
            !templateChanged &&
            !prefixesChanged &&
            !replaceModeChanged &&
            !appWillMutate)
        {
            return false;
        }

        if (requiresAllowPassingArgumentsEnable && !newApp.AllowPassingArguments)
            newApp.AllowPassingArguments = true;

        if (string.Equals(newApp.Id, oldAppId, StringComparison.OrdinalIgnoreCase) &&
            !templateChanged &&
            !prefixesChanged &&
            !replaceModeChanged)
        {
            Changed?.Invoke();
            return true;
        }

        handlerMappingService.RemoveHandlerMapping(key, oldAppId, database);
        handlerMappingService.SetHandlerMapping(
            key,
            new HandlerMappingEntry(
                newApp.Id,
                newTemplate,
                PathPrefixes: newPrefixes?.Count > 0 ? [..newPrefixes] : null,
                ReplacePrefixes: newReplacePrefixes),
            database);
        Changed?.Invoke();
        return true;
    }

    public HandlerMappingMutationOutcome EditDirectHandler(
        AppDatabase database,
        string key,
        DirectHandlerEntry currentEntry,
        DirectHandlerEntry newEntry)
    {
        var keysToRestore =
            (currentEntry.ClassName != null && newEntry.Command != null) ||
            (currentEntry.Command != null && newEntry.ClassName != null)
                ? new[] { key }
                : Array.Empty<string>();

        handlerMappingService.SetDirectHandlerMapping(key, newEntry, database);
        Changed?.Invoke();
        return new HandlerMappingMutationOutcome(keysToRestore);
    }

    public RemovedAppMappingState? RemoveMapping(AppDatabase database, AppMappingRowTag tag)
    {
        var allMappings = handlerMappingService.GetAllHandlerMappings(database);
        if (!allMappings.TryGetValue(tag.Key, out var entries))
            return null;

        var removedEntry = entries.FirstOrDefault(e =>
            string.Equals(e.AppId, tag.AppId, StringComparison.OrdinalIgnoreCase));
        if (removedEntry.AppId == null)
            return null;

        handlerMappingService.RemoveHandlerMapping(tag.Key, tag.AppId, database);
        var remaining = handlerMappingService.GetAllHandlerMappings(database);
        Changed?.Invoke();
        return new RemovedAppMappingState(tag.Key, removedEntry, RequiresRestore: !remaining.ContainsKey(tag.Key));
    }

    public RemovedDirectHandlerState? RemoveDirectHandler(AppDatabase database, DirectHandlerRowTag tag)
    {
        var directMappings = handlerMappingService.GetEffectiveDirectHandlerMappings(database);
        if (!directMappings.TryGetValue(tag.Key, out var removedEntry))
            return null;

        handlerMappingService.RemoveDirectHandlerMapping(tag.Key, database);
        Changed?.Invoke();
        return new RemovedDirectHandlerState(tag.Key, removedEntry, RequiresRestore: true);
    }

    public void ApplyImportedAssociations(AppDatabase database, IReadOnlyList<InteractiveAssociationEntry> entries)
    {
        foreach (var entry in entries)
            handlerMappingService.SetDirectHandlerMapping(entry.Key, entry.Handler, database);

        Changed?.Invoke();
    }

    public IReadOnlyList<string> SetAssociationsForApp(
        AppDatabase database,
        string appId,
        IReadOnlyList<HandlerAssociationItem> newAssociations)
    {
        var allMappings = handlerMappingService.GetAllHandlerMappings(database);

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
                handlerMappingService.RemoveHandlerMapping(key, appId, database);
                removedKeys.Add(key);
            }
        }

        var anyMutated = false;
        foreach (var item in newAssociations)
        {
            var isNew = !originalItems.ContainsKey(item.Key);
            var templateChanged = !isNew && originalItems[item.Key].ArgumentsTemplate != item.ArgumentsTemplate;
            var prefixesChanged = !isNew &&
                (!(originalItems[item.Key].PathPrefixes ?? []).SequenceEqual(item.PathPrefixes ?? [], StringComparer.OrdinalIgnoreCase) ||
                 originalItems[item.Key].ReplacePrefixes != item.ReplacePrefixes);

            if (!isNew && !templateChanged && !prefixesChanged)
                continue;

            handlerMappingService.SetHandlerMapping(
                item.Key,
                new HandlerMappingEntry(
                    appId,
                    item.ArgumentsTemplate,
                    PathPrefixes: item.PathPrefixes?.Count > 0 ? [..item.PathPrefixes] : null,
                    ReplacePrefixes: item.ReplacePrefixes),
                database);
            anyMutated = true;
        }

        if (anyMutated || removedKeys.Count > 0)
            Changed?.Invoke();

        return removedKeys;
    }

    public bool UpdateAppPrefixes(AppDatabase database, string appId, IReadOnlyList<string>? prefixes)
    {
        var app = database.Apps.FirstOrDefault(a => string.Equals(a.Id, appId, StringComparison.OrdinalIgnoreCase));
        if (app == null)
            return false;

        List<string>? normalizedPrefixes = prefixes?.Count > 0 ? [..prefixes] : null;
        if ((app.PathPrefixes ?? []).SequenceEqual(normalizedPrefixes ?? [], StringComparer.OrdinalIgnoreCase))
            return false;

        app.PathPrefixes = normalizedPrefixes;
        Changed?.Invoke();
        return true;
    }

    public void RestoreRemovedAppMapping(AppDatabase database, RemovedAppMappingState removed)
    {
        handlerMappingService.SetHandlerMapping(removed.Key, removed.Entry, database);
        Changed?.Invoke();
    }

    public void RestoreRemovedDirectHandler(AppDatabase database, RemovedDirectHandlerState removed)
    {
        handlerMappingService.SetDirectHandlerMapping(removed.Key, removed.Entry, database);
        Changed?.Invoke();
    }
}

public sealed record HandlerMappingMutationOutcome(IReadOnlyList<string> KeysToRestore);

public sealed record RemovedAppMappingState(
    string Key,
    HandlerMappingEntry Entry,
    bool RequiresRestore);

public sealed record RemovedDirectHandlerState(
    string Key,
    DirectHandlerEntry Entry,
    bool RequiresRestore);
