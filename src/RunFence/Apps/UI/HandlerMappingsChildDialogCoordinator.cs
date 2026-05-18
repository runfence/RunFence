using RunFence.Apps.UI.Forms;
using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public sealed class HandlerMappingsChildDialogCoordinator
{
    private readonly Func<IHandlerMappingAddDialog> _createAddDialog;
    private readonly Func<IImportAssociationsDialog> _createImportDialog;
    private readonly HandlerMappingDialogHelper _dialogHelper;
    private readonly HandlerMappingGridBuilder _gridBuilder;

    public HandlerMappingsChildDialogCoordinator(
        Func<IHandlerMappingAddDialog> createAddDialog,
        Func<IImportAssociationsDialog> createImportDialog,
        HandlerMappingDialogHelper dialogHelper,
        HandlerMappingGridBuilder gridBuilder)
    {
        _createAddDialog = createAddDialog;
        _createImportDialog = createImportDialog;
        _dialogHelper = dialogHelper;
        _gridBuilder = gridBuilder;
    }

    public IHandlerMappingAddDialog CreateAddDialog(IHandlerMappingDialogPersistence persistence)
    {
        var dialog = _createAddDialog();
        dialog.Initialize(persistence.GetDatabase().Apps, persistence);
        return dialog;
    }

    public IHandlerMappingAddDialog CreateEditAppDialog(
        AppMappingRowTag appTag,
        string? currentTemplateInRow,
        IHandlerMappingDialogPersistence persistence)
    {
        var database = persistence.GetDatabase();
        var currentApp = database.Apps.FirstOrDefault(app =>
            string.Equals(app.Id, appTag.AppId, StringComparison.OrdinalIgnoreCase));

        var dialog = _createAddDialog();
        dialog.InitializeForEditApp(
            appTag.Key,
            database.Apps,
            currentApp,
            appTag.AppId,
            currentTemplateInRow,
            persistence,
            currentApp?.PathPrefixes?.AsReadOnly(),
            appTag.PathPrefixes,
            appTag.ReplacePrefixes);
        return dialog;
    }

    public IHandlerMappingAddDialog? CreateEditDirectDialog(
        DirectHandlerRowTag tag,
        IHandlerMappingDialogPersistence persistence)
    {
        var currentEntry = _dialogHelper.GetCurrentDirectHandler(tag.Key, persistence);
        if (currentEntry == null)
            return null;

        var currentEntryValue = currentEntry.Value;
        var currentValue = currentEntryValue.ClassName ?? currentEntryValue.Command ?? string.Empty;
        var dialog = _createAddDialog();
        dialog.InitializeForEditDirect(tag.Key, currentValue, currentEntryValue, persistence);
        return dialog;
    }

    public IImportAssociationsDialog CreateImportDialog(
        IReadOnlyList<InteractiveAssociationEntry> entries,
        IHandlerMappingDialogPersistence persistence)
    {
        var dialog = _createImportDialog();
        dialog.Initialize(entries, _gridBuilder.GetExistingKeys(persistence.GetDatabase()), persistence);
        return dialog;
    }

    public HandlerMappingsChildDialogCloseResult HandleChildDialogClosed(
        DialogResult dialogResult,
        bool hasUnresolvedSubmitFailure,
        IHandlerMappingDialogPersistence persistence,
        IReadOnlySet<string> originalRunFenceKeys)
    {
        if (dialogResult == DialogResult.OK)
        {
            return new HandlerMappingsChildDialogCloseResult(
                ShouldRefresh: true,
                HasNewCapability: _dialogHelper.HasNewCapability(persistence, originalRunFenceKeys));
        }

        return new HandlerMappingsChildDialogCloseResult(
            ShouldRefresh: hasUnresolvedSubmitFailure,
            HasNewCapability: false);
    }
}
