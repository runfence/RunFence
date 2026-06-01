using RunFence.Apps.UI.Forms;

namespace RunFence.Apps.UI;

public sealed class HandlerAssociationMutationService(IExeAssociationRegistryReader reader)
    : IHandlerAssociationMutationService
{
    public IReadOnlyList<string> BuildSuggestions(
        string exePath,
        string? accountSid,
        IReadOnlyList<string> loadedKeys,
        IReadOnlyCollection<string> currentKeys,
        HandlerAssociationMode mode)
    {
        if (mode != HandlerAssociationMode.App)
            return [];

        var currentKeySet = currentKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fromRegistry = reader.GetHandledAssociations(exePath, accountSid).ToList();
        var removedLoadedKeys = loadedKeys
            .Except(fromRegistry, StringComparer.OrdinalIgnoreCase)
            .Except(currentKeySet, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var primary = fromRegistry
            .Except(currentKeySet, StringComparer.OrdinalIgnoreCase)
            .Concat(removedLoadedKeys)
            .ToList();

        return
        [
            .. primary,
            .. AppHandlerRegistrationService.CommonAssociationSuggestions
                .Except(primary, StringComparer.OrdinalIgnoreCase)
                .Except(currentKeySet, StringComparer.OrdinalIgnoreCase)
        ];
    }
}
