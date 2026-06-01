namespace RunFence.Acl;

public sealed record ProgramDataExplicitFileRequest(
    string Path,
    ProgramDataFileAclProfile Profile,
    IReadOnlyList<ProgramDataPrincipalAccess> AdditionalAccess,
    FileShare Share,
    bool OverwriteExisting);
