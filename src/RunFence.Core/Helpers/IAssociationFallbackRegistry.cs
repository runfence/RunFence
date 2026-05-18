namespace RunFence.Core.Helpers;

public interface IAssociationFallbackRegistry
{
    IOwnedAssociationRegistryRoot? OpenUserClassesRoot(string? targetSid = null);
    string? ReadFallbackCommand(IOwnedAssociationRegistryRoot root, string association);
    void WriteDefaultCommand(IOwnedAssociationRegistryRoot root, string association, string fallbackValue);
    void DeleteFallbackValue(IOwnedAssociationRegistryRoot root, string association);
    void DeleteExtensionCommandSubkeys(IOwnedAssociationRegistryRoot root, string association);
    void NotifyShellChanged();
}
