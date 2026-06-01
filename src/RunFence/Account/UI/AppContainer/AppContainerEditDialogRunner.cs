using RunFence.Core.Models;
using RunFence.Core;
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
        return CreateContainer(parent, standaloneWindow: false);
    }

    public AppContainerDialogRunResult CreateStandaloneContainer()
    {
        return CreateContainer(parent: null, standaloneWindow: true);
    }

    private AppContainerDialogRunResult CreateContainer(IWin32Window? parent, bool standaloneWindow)
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

        return RunDialog(existing: null, parent, showFirstContainerWarning: appContainerCount == 0, standaloneWindow);
    }

    public AppContainerDialogRunResult EditContainer(ContainerRow row, IWin32Window? parent)
    {
        return RunDialog(row.Container, parent, showFirstContainerWarning: false, standaloneWindow: false);
    }

    private AppContainerDialogRunResult RunDialog(
        AppContainerEntry? existing,
        IWin32Window? parent,
        bool showFirstContainerWarning,
        bool standaloneWindow)
    {
        using var dialog = dialogFactory();
        dialog.Initialize(existing);
        if (standaloneWindow)
        {
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.Shown += (_, _) =>
            {
                WindowForegroundHelper.ForceToForeground(dialog.Handle);
                dialog.BringToFront();
            };
        }

        var dialogResult = modalCoordinator.ShowModal(dialog, parent);
        return new AppContainerDialogRunResult(
            dialogResult,
            dialog.DeleteRequested,
            dialog.CreatedEntry,
            dialog.LastOperationStatus,
            showFirstContainerWarning && dialogResult == DialogResult.OK);
    }
}
