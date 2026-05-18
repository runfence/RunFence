using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Account.UI;

namespace RunFence.Account.UI.AppContainer;

public class AppContainerEditDialogRunner(
    IModalCoordinator modalCoordinator,
    Func<AppContainerEditDialog> dialogFactory,
    ILicenseService licenseService,
    IAccountMessageBoxService messageBoxService,
    ISessionProvider sessionProvider)
{
    public AppContainerDialogRunResult CreateContainer(IWin32Window? parent)
    {
        var session = sessionProvider.GetSession();
        var appContainerCount = session.Database.AppContainers.Count;
        if (!licenseService.CanCreateContainer(appContainerCount))
        {
            messageBoxService.Show(
                parent,
                licenseService.GetRestrictionMessage(EvaluationFeature.Containers, appContainerCount) ?? string.Empty,
                "License Limit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return new AppContainerDialogRunResult(DialogResult.None, DeleteRequested: false, CreatedEntry: null, OperationStatus: null, ShowFirstContainerWarning: false);
        }

        return RunDialog(existing: null, parent, showFirstContainerWarning: appContainerCount == 0);
    }

    public AppContainerDialogRunResult EditContainer(ContainerRow row, IWin32Window? parent)
    {
        return RunDialog(row.Container, parent, showFirstContainerWarning: false);
    }

    private AppContainerDialogRunResult RunDialog(
        AppContainerEntry? existing,
        IWin32Window? parent,
        bool showFirstContainerWarning)
    {
        using var dialog = dialogFactory();
        dialog.Initialize(existing);
        var dialogResult = modalCoordinator.ShowModal(dialog, parent);
        return new AppContainerDialogRunResult(
            dialogResult,
            dialog.DeleteRequested,
            dialog.CreatedEntry,
            dialog.LastOperationStatus,
            showFirstContainerWarning && dialogResult == DialogResult.OK);
    }
}
