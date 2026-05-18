namespace RunFence.Acl.UI;

public sealed class AclImportResult
{
    public static readonly AclImportResult Empty = new(false, []);

    public AclImportResult(bool anyAdded, IReadOnlyList<AclImportWarning> warnings)
    {
        AnyAdded = anyAdded;
        Warnings = warnings;
    }

    public bool AnyAdded { get; }
    public IReadOnlyList<AclImportWarning> Warnings { get; }
}
