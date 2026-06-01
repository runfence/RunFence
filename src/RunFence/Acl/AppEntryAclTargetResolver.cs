using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl;

public sealed class AppEntryAclTargetResolver : IAppEntryAclTargetResolver
{
    public string ResolveTargetPath(AppEntry app)
    {
        if (app.AclTarget == AclTarget.File)
            return Path.GetFullPath(app.ExePath);

        var folder = app.IsFolder
            ? Path.GetFullPath(app.ExePath)
            : Path.GetDirectoryName(Path.GetFullPath(app.ExePath))!;
        var cappedDepth = Math.Min(app.FolderAclDepth, PathConstants.MaxFolderAclDepth);
        for (int i = 0; i < cappedDepth; i++)
        {
            var parent = Path.GetDirectoryName(folder);
            if (parent == null)
                break;

            folder = parent;
        }

        return folder;
    }
}
