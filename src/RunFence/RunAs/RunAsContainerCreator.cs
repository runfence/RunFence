using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;

namespace RunFence.RunAs;

/// <summary>
/// Handles the "Create New Container" flow from the RunAs dialog.
/// </summary>
public class RunAsContainerCreator(
    IAppStateProvider appState,
    IDataChangeNotifier dataChangeNotifier,
    SessionContext session,
    IDatabaseService databaseService,
    RunAsAccountCreationUI creationUi,
    ILicenseService licenseService)
{
    /// <summary>
    /// Opens AppContainerEditDialog for inline container creation from the RunAs flow.
    /// Saves the new container to the database and returns it, or null if cancelled.
    /// </summary>
    public AppContainerEntry? CreateNewContainer()
    {
        if (!licenseService.CanCreateContainer(appState.Database.AppContainers.Count))
        {
            MessageBox.Show(licenseService.GetRestrictionMessage(EvaluationFeature.Containers, appState.Database.AppContainers.Count),
                "License Limit", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var newContainer = creationUi.ShowCreateContainerDialog();
        if (newContainer == null)
            return null;

        using var scope = session.PinDerivedKey.Unprotect();
        databaseService.SaveConfig(appState.Database, scope.Data, session.CredentialStore.ArgonSalt);
        dataChangeNotifier.NotifyDataChanged();
        return newContainer;
    }
}
