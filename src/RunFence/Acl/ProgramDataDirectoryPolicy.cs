namespace RunFence.Acl;

public sealed record ProgramDataDirectoryPolicy(
    string RelativePath,
    ProgramDataDirectoryAclProfile Profile,
    bool AllowsDynamicChildren);
