using RunFence.Apps.UI.Forms;
using RunFence.Infrastructure;

namespace RunFence.Apps.UI;

public sealed class HandlerAssociationsChildDialogCoordinator(
    Func<HandlerAssociationEditDialog> dialogFactory,
    IExeAssociationRegistryReader reader,
    IMessageBoxService messageBoxService,
    IModalCoordinator modalCoordinator)
{
    public HandlerAssociationItem? ShowAddDialog(
        IWin32Window? owner,
        IReadOnlyList<string> suggestions,
        string exePath,
        string? accountSid,
        IReadOnlyCollection<string> existingKeys)
    {
        using var dialog = dialogFactory();
        dialog.InitializeForAdd(suggestions, reader, exePath, accountSid);
        if (modalCoordinator.ShowModal(dialog, owner) != DialogResult.OK)
            return null;

        var key = dialog.SelectedKey;
        if (string.IsNullOrEmpty(key))
            return null;

        if (existingKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            messageBoxService.Show(
                owner,
                "This association is already in the list.",
                "Duplicate",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return null;
        }

        return new HandlerAssociationItem(
            key,
            dialog.NewTemplate,
            dialog.NewPrefixes,
            dialog.NewReplacePrefixes);
    }

    public HandlerAssociationItem? ShowEditDialog(
        IWin32Window? owner,
        HandlerAssociationItem item)
    {
        using var dialog = dialogFactory();
        dialog.Initialize(item.Key, item.ArgumentsTemplate, item.PathPrefixes, item.ReplacePrefixes);
        if (modalCoordinator.ShowModal(dialog, owner) != DialogResult.OK)
            return null;

        return new HandlerAssociationItem(
            item.Key,
            dialog.NewTemplate,
            dialog.NewPrefixes,
            dialog.NewReplacePrefixes);
    }
}
