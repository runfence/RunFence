using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public sealed record EditAppHandlerMappingSubmitRequest(
    string Key,
    AppEntry? SelectedApp,
    string? ArgumentsTemplate,
    IReadOnlyList<string>? AppPrefixes,
    IReadOnlyList<string>? PathPrefixes,
    bool ReplacePrefixes,
    string CurrentAppId,
    string? CurrentTemplateInRow,
    IReadOnlyList<string>? CurrentPathPrefixes,
    bool CurrentReplacePrefixes);
