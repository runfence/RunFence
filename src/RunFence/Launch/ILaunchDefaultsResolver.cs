namespace RunFence.Launch;

public interface ILaunchDefaultsResolver
{
    /// <summary>
    /// Fills PrivilegeLevel from the account's stored default when null.
    /// Does NOT resolve credentials. Used by LaunchFacade before permission grant checks.
    /// Uses Visit dispatch internally — no type-checking.
    /// </summary>
    LaunchIdentity ResolveDefaults(LaunchIdentity identity);
}
