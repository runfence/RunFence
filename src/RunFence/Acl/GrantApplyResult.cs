namespace RunFence.Acl;

public readonly struct GrantApplyResult
{
    private readonly IReadOnlyList<GrantApplyWarning>? _warnings;

    public GrantApplyResult(
        bool GrantApplied = false,
        bool TraverseApplied = false,
        bool DatabaseModified = false,
        bool DurableSaveCompleted = false,
        IReadOnlyList<GrantApplyWarning>? Warnings = null)
    {
        this.GrantApplied = GrantApplied;
        this.TraverseApplied = TraverseApplied;
        this.DatabaseModified = DatabaseModified;
        this.DurableSaveCompleted = DurableSaveCompleted;
        _warnings = Warnings;
    }

    public bool GrantApplied { get; }

    public bool TraverseApplied { get; }

    public bool DatabaseModified { get; }

    public bool DurableSaveCompleted { get; }

    public IReadOnlyList<GrantApplyWarning> Warnings => _warnings ?? [];
}
