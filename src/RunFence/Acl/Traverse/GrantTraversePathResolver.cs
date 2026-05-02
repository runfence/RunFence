namespace RunFence.Acl.Traverse;

public sealed class GrantTraversePathResolver(IFileSystemPathInfo pathInfo)
{
    public string? GetTraversePath(string grantPath)
        => pathInfo.DirectoryExists(grantPath) ? grantPath : Path.GetDirectoryName(grantPath);
}
