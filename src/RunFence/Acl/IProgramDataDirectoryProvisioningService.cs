namespace RunFence.Acl;

public interface IProgramDataDirectoryProvisioningService
{
    string EnsureRoot();
    string EnsureKnownDirectory(ProgramDataDirectoryPolicy policy);
    void EnsureKnownDirectoryTreeInheritsFromRoot(ProgramDataDirectoryPolicy policy);
    void EnsureDirectoryTreeInheritsFromRoot(string directoryPath, ProgramDataDirectoryAclProfile rootAclProfile);
    void EnsureTraverseOnlyAccess(string directoryPath, string sid, ProgramDataDirectoryAclProfile aclProfile);
}
