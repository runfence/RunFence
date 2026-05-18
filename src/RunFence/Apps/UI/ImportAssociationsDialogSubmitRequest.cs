using RunFence.Apps;

namespace RunFence.Apps.UI;

public sealed record ImportAssociationsDialogSubmitRequest(
    IReadOnlyList<InteractiveAssociationEntry> SelectedEntries);
