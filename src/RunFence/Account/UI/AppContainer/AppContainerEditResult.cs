using RunFence.Core.Models;

namespace RunFence.Account.UI.AppContainer;

public sealed record AppContainerEditResult(
    AppContainerOperationStatus Status,
    AppContainerEntry Entry,
    bool CapabilitiesChanged,
    string? ErrorMessage,
    IReadOnlyList<string> Warnings);
