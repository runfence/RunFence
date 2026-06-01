using RunFence.Apps.UI.Forms;

namespace RunFence.Apps.UI;

public interface IHandlerAssociationMutationService
{
    IReadOnlyList<string> BuildSuggestions(
        string exePath,
        string? accountSid,
        IReadOnlyList<string> loadedKeys,
        IReadOnlyCollection<string> currentKeys,
        HandlerAssociationMode mode);
}
