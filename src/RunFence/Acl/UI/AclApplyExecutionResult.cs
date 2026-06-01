namespace RunFence.Acl.UI;

public sealed class AclApplyExecutionResult
{
    private readonly HashSet<CompletedOperation> _completedOperations = new(CompletedOperationComparer.Instance);

    public List<AclApplyError> Errors { get; } = [];

    public List<GrantApplyWarning> Warnings { get; } = [];

    public AclApplyFatalFailure? FatalFailure { get; private set; }

    public bool HasFatalFailure => FatalFailure != null;

    public bool WasCanceled { get; set; }

    public void MarkCompleted(AclPendingOperationKind operationKind, string path, bool? isDeny)
    {
        _completedOperations.Add(new CompletedOperation(operationKind, path, isDeny));
    }

    public bool WasCompleted(AclPendingOperationKind operationKind, string path, bool? isDeny)
        => _completedOperations.Contains(new CompletedOperation(operationKind, path, isDeny));

    public bool HasError(AclPendingOperationKind operationKind, string path, bool? isDeny)
        => Errors.Any(error =>
            error.OperationKind == operationKind &&
            error.IsDeny == isDeny &&
            string.Equals(error.Path, path, StringComparison.OrdinalIgnoreCase));

    public void SetFatalFailure(AclApplyFatalFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        FatalFailure = failure;
    }

    private readonly record struct CompletedOperation(
        AclPendingOperationKind OperationKind,
        string Path,
        bool? IsDeny);

    private sealed class CompletedOperationComparer : IEqualityComparer<CompletedOperation>
    {
        public static CompletedOperationComparer Instance { get; } = new();

        public bool Equals(CompletedOperation x, CompletedOperation y)
            => x.OperationKind == y.OperationKind
               && x.IsDeny == y.IsDeny
               && string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(CompletedOperation obj)
            => HashCode.Combine(
                obj.OperationKind,
                obj.IsDeny,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Path));
    }
}
