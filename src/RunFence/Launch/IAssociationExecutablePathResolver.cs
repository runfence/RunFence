namespace RunFence.Launch;

public interface IAssociationExecutablePathResolver
{
    AssociationExecutablePathResolution Resolve(string exePath);
}
