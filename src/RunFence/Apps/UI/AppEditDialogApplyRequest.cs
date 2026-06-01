using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

public sealed record AppEditDialogApplyRequest(
    AppEntry Result,
    AppDatabase? Database,
    string? SelectedConfigPath,
    IReadOnlyList<HandlerAssociationItem> CurrentAssociations,
    Func<AppEditDialogApplyContext, Task> ApplyAsync);
