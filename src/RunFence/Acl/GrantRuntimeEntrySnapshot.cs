using RunFence.Core.Models;

namespace RunFence.Acl;

public sealed class GrantRuntimeEntrySnapshot
{
    public GrantRuntimeEntrySnapshot(
        string sid,
        string path,
        bool isTraverseOnly,
        bool isDeny,
        GrantedPathEntry? entry)
    {
        Sid = sid;
        Path = System.IO.Path.GetFullPath(path);
        IsTraverseOnly = isTraverseOnly;
        IsDeny = isDeny;
        Entry = entry?.Clone();
    }

    public string Sid { get; }

    public string Path { get; }

    public bool IsTraverseOnly { get; }

    public bool IsDeny { get; }

    public GrantedPathEntry? Entry { get; }
}
