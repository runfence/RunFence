using System.Security.AccessControl;
using RunFence.Acl;

namespace RunFence.Launch;

public sealed class FolderHandlerRegistrationAccessService(
    IGrantMutatorService grantMutatorService)
{
    public bool EnsureLauncherDirectoryAccess(
        string accountSid,
        string launcherDir,
        FolderHandlerRegistrationEffects effects,
        List<string> warnings)
    {
        TryEnsureRegistrationAccess(
            accountSid,
            launcherDir,
            effects,
            warnings,
            isAccountGrant: true,
            out var accountSaveFailure);
        TryEnsureRegistrationAccess(
            AclHelper.LowIntegritySid,
            launcherDir,
            effects,
            warnings,
            isAccountGrant: false,
            out var lowIntegritySaveFailure);
        return accountSaveFailure || lowIntegritySaveFailure;
    }

    private void TryEnsureRegistrationAccess(
        string sid,
        string launcherDir,
        FolderHandlerRegistrationEffects effects,
        List<string> warnings,
        bool isAccountGrant,
        out bool saveFailure)
    {
        saveFailure = false;
        try
        {
            var result = grantMutatorService.EnsureAccess(
                sid,
                launcherDir,
                FileSystemRights.ReadAndExecute,
                confirm: null,
                unelevated: true);
            if (isAccountGrant)
            {
                effects.AccountGrantApplied = result.GrantApplied;
                effects.AccountTraverseApplied = result.TraverseApplied;
            }
            else
            {
                effects.LowIntegrityGrantApplied = result.GrantApplied;
                effects.LowIntegrityTraverseApplied = result.TraverseApplied;
            }

            AppendGrantWarnings(warnings, result.Warnings);
        }
        catch (GrantOperationException ex) when (GrantApplyFailureFormatter.IsSaveFailureStep(ex.Step))
        {
            saveFailure = true;
            warnings.Add(GrantApplyFailureFormatter.Format(new GrantApplyFailure(ex.Step, ex.Path, ex.ConfigPath, ex.Cause)));
        }
    }

    private static void AppendGrantWarnings(List<string> warnings, IReadOnlyList<GrantApplyWarning> grantWarnings)
    {
        foreach (var warning in grantWarnings)
            warnings.Add(GrantApplyFailureFormatter.Format(warning));
    }
}
