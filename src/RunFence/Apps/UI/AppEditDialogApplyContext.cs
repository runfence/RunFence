using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public sealed record AppEditDialogApplyContext(
    AppEntry Result,
    AppEntry? PreviousApp,
    AppEntryChangeSet ChangeSet,
    string? PreviousConfigPath,
    string? SelectedConfigPath,
    IReadOnlyList<HandlerAssociationItem> CurrentAssociations);
