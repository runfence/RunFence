using RunFence.Core.Models;

namespace RunFence.Account.UI.AppContainer;

public sealed record AppContainerEditSubmitResult
{
    public DialogResult? DialogResult { get; init; }
    public AppContainerEntry? CreatedEntry { get; init; }
    public string? ValidationMessage { get; init; }
    public AppContainerOperationStatus? OperationStatus { get; init; }
    public bool RestartRequired { get; init; }
    public string? PersistenceWarningText { get; init; }
    public string? OperationErrorText { get; init; }
    public IReadOnlyList<string> ComAccessWarnings { get; init; } = [];
}
