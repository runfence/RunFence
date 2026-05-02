using RunFence.Persistence;

namespace RunFence.Acl;

/// <summary>
/// Resolves AppContainer SIDs from the database. Separated from <see cref="AclHelper"/>
/// to keep the static utility class free of database dependencies.
/// </summary>
public class ContainerLookupHelper(IDatabaseProvider databaseProvider)
{
    /// <summary>
    /// Returns the SID string for the AppContainer with the given name, or null if not found or empty.
    /// </summary>
    public string? ResolveContainerSid(string containerName)
    {
        var db = databaseProvider.GetDatabase();
        var container = db.AppContainers.FirstOrDefault(c =>
            string.Equals(c.Name, containerName, StringComparison.OrdinalIgnoreCase));
        var sid = container?.Sid;
        return string.IsNullOrEmpty(sid) ? null : sid;
    }
}
