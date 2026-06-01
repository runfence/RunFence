namespace RunFence.Launch;

using RunFence.Infrastructure;

public sealed class AssociationExecutablePathResolver(
    IBackupIntentFileSystem fileSystem,
    IWindowsAppsPackagePathRepairer packagePathRepairer) : IAssociationExecutablePathResolver
{
    public AssociationExecutablePathResolution Resolve(string exePath)
    {
        if (!Path.IsPathRooted(exePath))
            return AssociationExecutablePathResolution.Valid(exePath);

        var state = fileSystem.GetFileState(exePath);
        if (state == BackupIntentPathState.Exists)
            return AssociationExecutablePathResolution.Valid(exePath);

        if (state == BackupIntentPathState.Unknown)
            return AssociationExecutablePathResolution.Invalid(exePath, "rooted executable path is inaccessible or unreadable");

        var repairedPath = packagePathRepairer.TryRepair(exePath);
        if (repairedPath != null)
            return AssociationExecutablePathResolution.Valid(repairedPath, wasRepaired: true);

        return AssociationExecutablePathResolution.Invalid(exePath, "rooted executable path does not exist");
    }
}
