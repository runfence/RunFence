using RunFence.Core.Models;

namespace RunFence.Acl.UI;

public static class AclPendingKeys
{
    public static (string Path, bool IsDeny) Grant(GrantedPathEntry entry)
        => Grant(entry.Path, entry.IsDeny);

    public static (string Path, bool IsDeny) Grant(string path, bool isDeny)
        => (Path.GetFullPath(path), isDeny);

    public static string Traverse(GrantedPathEntry entry)
        => Traverse(entry.Path);

    public static string Traverse(string path)
        => Path.GetFullPath(path);
}
