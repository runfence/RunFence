using RunFence.Core.Models;

namespace RunFence.Account.UI.AppContainer;

public sealed record AppContainerDialogRunResult(
    DialogResult DialogResult,
    bool DeleteRequested,
    AppContainerEntry? CreatedEntry,
    AppContainerOperationStatus? OperationStatus,
    bool ShowFirstContainerWarning);
