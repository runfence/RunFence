using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public class AppEditDialogAclConfigBuilder(AclConfigValidator validator)
{
    public AppEditDialogAclBuildResult Build(
        AppEditDialogInputSnapshot snapshot,
        string exePath,
        bool isFolder)
    {
        var aclSnapshot = snapshot.AclConfig;
        var aclTarget = isFolder ? AclTarget.Folder : aclSnapshot.SelectedAclTarget;
        var depth = aclTarget == AclTarget.Folder ? aclSnapshot.FolderAclDepth : 0;
        var existingApps = snapshot.ExistingApps.ToList();
        var isAllowMode = aclSnapshot.AclMode == AclMode.Allow;

        string? CheckPathConflict() => validator.CheckPathConflict(
            exePath,
            isFolder,
            isAllowMode,
            aclTarget,
            depth,
            existingApps,
            snapshot.ExistingApp?.Id);

        var error = validator.Validate(
            exePath,
            isFolder,
            aclSnapshot.RestrictAcl,
            isAllowMode,
            aclTarget,
            depth,
            aclSnapshot.AllowedEntries.Count,
            CheckPathConflict);
        if (error != null)
            return new AppEditDialogAclBuildResult(null, error);

        var restrictAcl = aclSnapshot.RestrictAcl && !PathHelper.IsUrlScheme(exePath);
        List<AllowAclEntry>? allowedEntries = null;
        if (restrictAcl && isAllowMode)
        {
            allowedEntries = aclSnapshot.AllowedEntries
                .Select(entry => new AllowAclEntry
                {
                    Sid = entry.Sid,
                    AllowExecute = entry.AllowExecute,
                    AllowWrite = entry.AllowWrite
                })
                .ToList();
        }

        return new AppEditDialogAclBuildResult(
            new AclConfigResult(
                restrictAcl,
                aclSnapshot.AclMode,
                aclTarget,
                depth,
                aclSnapshot.DeniedRights,
                allowedEntries),
            null);
    }
}
