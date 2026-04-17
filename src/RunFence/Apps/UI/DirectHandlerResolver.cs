using RunFence.Apps;
using RunFence.Core.Models;

namespace RunFence.Apps.UI;

/// <summary>
/// Resolves a user-supplied handler value string into a typed <see cref="DirectHandlerEntry"/>.
/// Uses the registry reader to determine whether a value is a registered ProgId (class-based)
/// or a command string.
/// </summary>
public class DirectHandlerResolver(
    IExeAssociationRegistryReader reader,
    IInteractiveUserAssociationReader interactiveReader)
{
    /// <summary>
    /// Returns the <see cref="DirectHandlerEntry"/> for the given association key and handler value.
    /// Extensions with a registered ProgId produce a class-based entry; all others produce a command entry.
    /// </summary>
    public DirectHandlerEntry ResolveDirectHandlerEntry(string key, string handlerValue)
    {
        if (key.StartsWith('.') && reader.IsRegisteredProgId(key, handlerValue))
            return new DirectHandlerEntry { ClassName = handlerValue };

        return new DirectHandlerEntry { Command = handlerValue };
    }

    /// <summary>
    /// Returns all associations set by the interactive user in HKCU.
    /// </summary>
    public IReadOnlyList<InteractiveAssociationEntry> GetInteractiveUserAssociations() =>
        interactiveReader.GetInteractiveUserAssociations();
}
