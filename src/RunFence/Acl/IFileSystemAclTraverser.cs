namespace RunFence.Acl;

/// <summary>
/// Traverses a filesystem tree and yields the ACL security descriptor for each visited path.
/// Abstracted for testability — the default implementation uses <see cref="FileSystemAclTraverser"/>.
/// </summary>
public interface IFileSystemAclTraverser
{
    IEnumerable<AclTraversalEntry> Traverse(
        IReadOnlyList<string> rootPaths,
        IProgress<long> progress,
        CancellationToken ct);
}
