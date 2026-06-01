namespace RunFence.Acl;

public readonly record struct ProgramDataSecurityRepairResult(
    bool OwnerChanged,
    bool RemovedUntrustedWriteOrOwnerAccess,
    bool DaclChanged);
