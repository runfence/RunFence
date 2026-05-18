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

        if (fileSystem.FileExists(exePath))
            return AssociationExecutablePathResolution.Valid(exePath);

        var repairedPath = packagePathRepairer.TryRepair(exePath);
        if (repairedPath != null)
            return AssociationExecutablePathResolution.Valid(repairedPath, wasRepaired: true);

        return AssociationExecutablePathResolution.Invalid(exePath, "rooted executable path does not exist");
    }
}
