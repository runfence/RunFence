namespace RunFence.RunAs;

public enum RunAsCreatedAccountPersistenceStatus
{
    Succeeded,
    CleanupStateSaveFailed,
    CredentialSaveRolledBack,
    CredentialSaveRollbackFailed,
    PrePersistenceRolledBack,
    PrePersistenceRollbackFailed
}
