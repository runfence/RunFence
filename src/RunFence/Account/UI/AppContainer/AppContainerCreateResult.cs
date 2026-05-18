using RunFence.Core.Models;

namespace RunFence.Account.UI.AppContainer;

public sealed record AppContainerCreateResult(
    AppContainerOperationStatus Status,
    AppContainerEntry? Entry,
    string? ErrorMessage,
    IReadOnlyList<string> Warnings);
