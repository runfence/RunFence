using RunFence.Account.UI;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.RunAs;

/// <summary>
/// Handles the "Create New Container" flow from the RunAs dialog.
/// </summary>
public class RunAsContainerCreator(
    IAppStateProvider appState,
    IDataChangeNotifier dataChangeNotifier,
    IRunAsContainerCreationUI creationUi,
    IAccountMessageBoxService messageBoxService,
    ILicenseService licenseService) : IRunAsContainerCreator
{
    /// <summary>
    /// Opens AppContainerEditDialog for inline container creation from the RunAs flow.
    /// Returns the created container entry or null if cancelled.
    /// </summary>
    public AppContainerEntry? CreateNewContainer()
    {
        if (!licenseService.CanCreateContainer(appState.Database.AppContainers.Count))
        {
            messageBoxService.Show(
                owner: null,
                licenseService.GetRestrictionMessage(EvaluationFeature.Containers, appState.Database.AppContainers.Count) ?? string.Empty,
                "License Limit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return null;
        }

        var newContainer = creationUi.ShowCreateContainerDialog();
        if (newContainer == null)
            return null;

        dataChangeNotifier.NotifyDataChanged();
        return newContainer;
    }
}
