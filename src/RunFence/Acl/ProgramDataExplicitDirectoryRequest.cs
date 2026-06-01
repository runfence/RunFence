namespace RunFence.Acl;

public sealed record ProgramDataExplicitDirectoryRequest(
    string Path,
    ProgramDataDirectoryAclProfile Profile,
    IReadOnlyList<ProgramDataPrincipalAccess> AdditionalAccess,
    bool ReplaceExistingSecurity);
