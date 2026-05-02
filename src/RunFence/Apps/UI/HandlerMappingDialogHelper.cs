using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Pure data helper for <see cref="HandlerMappingsDialog"/>: validates handler keys,
/// resolves direct handler entries, looks up current direct handlers, and detects new capabilities.
/// Contains no dialog state — all dialog-state is passed as parameters.
/// </summary>
public class HandlerMappingDialogHelper(
    IExeAssociationRegistryReader reader,
    IHandlerMappingService handlerMappingService)
{
    /// <summary>
    /// Validates keys against <see cref="AppHandlerRegistrationService.IsValidKey"/>.
    /// Returns a tuple of valid and invalid key lists without showing any UI.
    /// </summary>
    public (List<string> Valid, List<string> Invalid) ValidateKeys(IReadOnlyList<string> keys)
    {
        var valid = new List<string>(keys.Count);
        var invalid = new List<string>();
        foreach (var key in keys)
        {
            if (AppHandlerRegistrationService.IsValidKey(key))
                valid.Add(key);
            else
                invalid.Add(key);
        }
        return (valid, invalid);
    }

    /// <summary>
    /// Resolves a user-supplied handler value string into a typed <see cref="DirectHandlerEntry"/>.
    /// Extensions with a registered ProgId produce a class-based entry; all others produce a command entry.
    /// </summary>
    public DirectHandlerEntry ResolveDirectHandlerEntry(string key, string handlerValue)
    {
        if (key.StartsWith('.') && reader.IsRegisteredProgId(key, handlerValue))
            return new DirectHandlerEntry { ClassName = handlerValue };

        return new DirectHandlerEntry { Command = handlerValue };
    }

    /// <summary>
    /// Returns the current effective direct handler entry for the given key, or null if none exists.
    /// </summary>
    public DirectHandlerEntry? GetCurrentDirectHandler(string key, Func<AppDatabase> getDatabase)
    {
        var mappings = handlerMappingService.GetEffectiveDirectHandlerMappings(getDatabase());
        return mappings.TryGetValue(key, out var entry) ? entry : null;
    }

    /// <summary>
    /// Returns true when any handler keys have been newly added since the dialog was initialized
    /// (used to prompt the user to open Default Apps).
    /// </summary>
    public bool HasNewCapability(Func<AppDatabase> getDatabase, HashSet<string> originalRunFenceKeys)
    {
        var currentKeys = handlerMappingService.GetAllHandlerMappings(getDatabase()).Keys;
        return currentKeys.Any(k => !originalRunFenceKeys.Contains(k));
    }
}
