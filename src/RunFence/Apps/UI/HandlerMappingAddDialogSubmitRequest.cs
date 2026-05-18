using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public sealed record HandlerMappingAddDialogSubmitRequest(
    bool IsDirectMode,
    IReadOnlyList<string> ResolvedKeys,
    AppEntry? SelectedApp,
    string? DirectHandlerValue,
    string? ArgumentsTemplate,
    IReadOnlyList<string>? AppPrefixes,
    IReadOnlyList<string>? PathPrefixes,
    bool ReplacePrefixes);
