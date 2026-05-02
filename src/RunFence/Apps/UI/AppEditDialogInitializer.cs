using RunFence.Acl.UI;
using RunFence.Core.Models;
using RunFence.Persistence;
using System.Collections.ObjectModel;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles domain-state assembly for <see cref="AppEditDialog"/> initialization.
/// </summary>
public class AppEditDialogInitializer(
    AppEditPopulator appEditPopulator,
    IAppEntryIdGenerator idGenerator,
    IAppConfigService appConfigService,
    AppEditAssociationHandler associationHandler)
{
    /// <summary>
    /// Generates a unique app ID from the existing app ID set and returns it.
    /// </summary>
    public string PreGenerateId(IEnumerable<string> existingIds)
        => idGenerator.GenerateUniqueId(existingIds);

    public AppEditInitializationModel CreateExistingInitializationModel(
        AppEntry app,
        AppDatabase? database)
    {
        var state = appEditPopulator.LoadExistingApp(app);
        var associations = database != null && !string.IsNullOrEmpty(app.Id)
            ? associationHandler.GetCurrentAssociations(app.Id)
            : null;

        return new AppEditInitializationModel(
            State: state,
            AclState: new AclConfigInitializationModel(
                RestrictAcl: app.RestrictAcl,
                AclMode: app.AclMode,
                DeniedRights: app.DeniedRights,
                AllowedAclEntries: app.AllowedAclEntries?.ToList().AsReadOnly(),
                AclTarget: app.AclTarget,
                FolderAclDepth: app.FolderAclDepth),
            AccountSelection: new AppEditExistingAccountSelection(app.AccountSid, app.AppContainerName),
            SelectedConfigPath: appConfigService.GetConfigPath(app.Id),
            IpcCallers: app.AllowedIpcCallers?.ToList().AsReadOnly(),
            EnvironmentVariables: app.EnvironmentVariables != null
                ? new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(app.EnvironmentVariables, StringComparer.OrdinalIgnoreCase))
                : null,
            Associations: associations,
            PathPrefixes: app.PathPrefixes?.ToList().AsReadOnly());
    }

    public IReadOnlyList<HandlerAssociationItem>? GetAssociations(
        AppDatabase? database,
        string? appId)
    {
        if (database == null || string.IsNullOrEmpty(appId))
            return null;
        return associationHandler.GetCurrentAssociations(appId);
    }

}
