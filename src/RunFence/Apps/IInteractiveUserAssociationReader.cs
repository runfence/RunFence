using RunFence.Core.Models;

namespace RunFence.Apps;

/// <summary>
/// An association found in the interactive user's HKCU registry.
/// </summary>
public record InteractiveAssociationEntry(
    string Key,
    DirectHandlerEntry Handler,
    string Description);

/// <summary>
/// Reads the interactive user's HKCU association overrides for import into RunFence direct handler mappings.
/// </summary>
public interface IInteractiveUserAssociationReader
{
    /// <summary>
    /// Returns all user-specific association overrides from the interactive user's HKCU.
    /// Results are cached for the lifetime of this instance.
    /// </summary>
    IReadOnlyList<InteractiveAssociationEntry> GetInteractiveUserAssociations();

    /// <summary>
    /// Resolves the interactive user's handler for a single association key.
    /// Returns null if no HKCU override exists or it cannot be resolved.
    /// </summary>
    DirectHandlerEntry? GetAssociationHandler(string key);
}
