namespace RunFence.Acl;

public sealed record ProgramDataFilePolicy(
    string RelativePath,
    ProgramDataFileAclProfile Profile);
