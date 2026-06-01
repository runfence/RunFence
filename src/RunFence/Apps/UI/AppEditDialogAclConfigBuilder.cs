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
        var validationState = validator.ValidateState(
            exePath,
            isFolder,
            aclSnapshot.RestrictAcl,
            aclSnapshot.AclMode == AclMode.Allow,
            aclSnapshot.SelectedAclTarget,
            aclSnapshot.FolderAclDepth,
            aclSnapshot.AllowedEntries.Count,
            snapshot.ExistingApps.ToList(),
            snapshot.ExistingApp?.Id);
        if (!validationState.IsValid)
            return new AppEditDialogAclBuildResult(null, validationState.ConflictMessage);

        List<AllowAclEntry>? allowedEntries = null;
        if (validationState.RestrictAcl && validationState.IsAllowMode)
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
                validationState.RestrictAcl,
                validationState.AclMode,
                validationState.SelectedAclTarget,
                validationState.FolderAclDepth,
                aclSnapshot.DeniedRights,
                allowedEntries),
            null);
    }
}
