using System.Security.AccessControl;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl;

public class GrantMutatorService(
    GrantAccessEnsurer accessEnsurer,
    GrantFileSystemOperations fileSystemOperations,
    PersistedGrantMutationWorkflow persistedGrantMutationWorkflow) : IGrantMutatorService
{
    public GrantApplyResult AddGrant(string accountSid, string path, bool isDeny,
        SavedRightsState? savedRights, Func<bool>? confirm, IGrantIntentStore? store = null)
        => persistedGrantMutationWorkflow.AddGrant(
            accountSid,
            path,
            isDeny,
            savedRights ?? SavedRightsState.DefaultForMode(isDeny),
            confirm,
            store);

    public GrantApplyResult EnsureAccess(string sid, string path, SavedRightsState savedRights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
        => accessEnsurer.EnsureAccess(sid, path, savedRights, confirm, unelevated);

    public GrantApplyResult EnsureAccess(string sid, string path, FileSystemRights rights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
        => accessEnsurer.EnsureAccess(sid, path, rights, confirm, unelevated);

    public GrantApplyResult EnsureTemporaryAccess(string sid, string path, SavedRightsState savedRights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
        => accessEnsurer.EnsureTemporaryAccess(sid, path, savedRights, confirm, unelevated);

    public GrantApplyResult EnsureTemporaryAccess(string sid, string path, FileSystemRights rights,
        Func<string, string, bool>? confirm = null, bool unelevated = false)
        => accessEnsurer.EnsureTemporaryAccess(sid, path, rights, confirm, unelevated);

    public GrantApplyResult RemoveGrant(string accountSid, string path, bool isDeny)
        => persistedGrantMutationWorkflow.RemoveGrant(accountSid, path, isDeny);

    public GrantApplyResult RestoreGrant(string accountSid, string path, bool isDeny,
        GrantIntentRestoreSnapshot previousState)
        => persistedGrantMutationWorkflow.RestoreGrant(accountSid, path, isDeny, previousState);

    public GrantApplyResult UpdateGrant(string accountSid, string path, bool isDeny,
        SavedRightsState savedRights, Func<bool>? confirm, IGrantIntentStore? store = null)
        => persistedGrantMutationWorkflow.UpdateGrant(accountSid, path, isDeny, savedRights, confirm, store);

    public GrantApplyResult SwitchGrantMode(string accountSid, string path, bool newIsDeny,
        SavedRightsState savedRights, Func<bool>? confirm, IGrantIntentStore? store = null)
        => persistedGrantMutationWorkflow.SwitchGrantMode(accountSid, path, newIsDeny, savedRights, confirm, store);

    public GrantApplyResult UntrackGrant(string accountSid, string path, bool isDeny)
        => persistedGrantMutationWorkflow.UntrackGrant(accountSid, path, isDeny);

    public void FixGrant(string sid, string path, bool isDeny)
        => fileSystemOperations.FixGrant(sid, path, isDeny);

    public GrantApplyResult FixGrantAcl(string accountSid, string path, bool isDeny)
        => persistedGrantMutationWorkflow.FixGrantAcl(accountSid, path, isDeny);
}
