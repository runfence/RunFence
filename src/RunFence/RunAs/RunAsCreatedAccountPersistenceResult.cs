using RunFence.Core.Models;

namespace RunFence.RunAs;

public sealed record RunAsCreatedAccountPersistenceResult(
    RunAsCreatedAccountPersistenceStatus Status,
    CredentialEntry? Credential,
    bool DataChangeNotified,
    string? ErrorMessage = null,
    string? RollbackErrorMessage = null);
