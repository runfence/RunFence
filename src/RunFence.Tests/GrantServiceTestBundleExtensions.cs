using System.Security.AccessControl;
using RunFence.Acl;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Tests;

internal static class GrantServiceTestBundleExtensions
{
    public static GrantApplyResult AddGrant(
        this GrantServiceTestBundle service,
        string sid,
        string path,
        bool isDeny,
        SavedRightsState? savedRights = null,
        Func<bool>? confirm = null,
        IGrantIntentStore? store = null)
        => service.GrantMutator.AddGrant(sid, path, isDeny, savedRights, confirm, store);

    public static GrantApplyResult EnsureAccess(
        this GrantServiceTestBundle service,
        string sid,
        string path,
        SavedRightsState savedRights,
        Func<string, string, bool>? confirm = null,
        bool unelevated = false)
        => service.GrantMutator.EnsureAccess(sid, path, savedRights, confirm, unelevated);

    public static GrantApplyResult EnsureAccess(
        this GrantServiceTestBundle service,
        string sid,
        string path,
        FileSystemRights rights,
        Func<string, string, bool>? confirm = null,
        bool unelevated = false)
        => service.GrantMutator.EnsureAccess(sid, path, rights, confirm, unelevated);

    public static GrantApplyResult UpdateGrant(
        this GrantServiceTestBundle service,
        string sid,
        string path,
        bool isDeny,
        SavedRightsState savedRights,
        Func<bool>? confirm = null,
        IGrantIntentStore? store = null)
        => service.GrantMutator.UpdateGrant(sid, path, isDeny, savedRights, confirm, store);

    public static GrantApplyResult SwitchGrantMode(
        this GrantServiceTestBundle service,
        string sid,
        string path,
        bool newIsDeny,
        SavedRightsState savedRights,
        Func<bool>? confirm = null,
        IGrantIntentStore? store = null)
        => service.GrantMutator.SwitchGrantMode(sid, path, newIsDeny, savedRights, confirm, store);

    public static GrantApplyResult RemoveGrant(this GrantServiceTestBundle service, string sid, string path, bool isDeny)
        => service.GrantMutator.RemoveGrant(sid, path, isDeny);

    public static GrantApplyResult RestoreGrant(
        this GrantServiceTestBundle service,
        string sid,
        string path,
        bool isDeny,
        GrantIntentRestoreSnapshot previousState)
        => service.GrantMutator.RestoreGrant(sid, path, isDeny, previousState);

    public static GrantApplyResult UntrackGrant(this GrantServiceTestBundle service, string sid, string path, bool isDeny)
        => service.GrantMutator.UntrackGrant(sid, path, isDeny);

    public static void FixGrant(this GrantServiceTestBundle service, string sid, string path, bool isDeny)
        => service.GrantMutator.FixGrant(sid, path, isDeny);

    public static GrantApplyResult FixGrantAcl(this GrantServiceTestBundle service, string sid, string path, bool isDeny)
        => service.GrantMutator.FixGrantAcl(sid, path, isDeny);

    public static GrantApplyResult AddTraverse(this GrantServiceTestBundle service, string sid, string path, IGrantIntentStore? store = null)
        => service.Traverse.AddTraverse(sid, path, store);

    public static GrantApplyResult RemoveTraverse(this GrantServiceTestBundle service, string sid, string path)
        => service.Traverse.RemoveTraverse(sid, path);

    public static GrantApplyResult RestoreTraverse(
        this GrantServiceTestBundle service,
        string sid,
        string path,
        GrantIntentRestoreSnapshot previousState)
        => service.Traverse.RestoreTraverse(sid, path, previousState);

    public static GrantApplyResult UntrackTraverse(this GrantServiceTestBundle service, string sid, string path)
        => service.Traverse.UntrackTraverse(sid, path);

    public static void CleanupOrphanedTraverse(this GrantServiceTestBundle service, string sid, string path)
        => service.Traverse.CleanupOrphanedTraverse(sid, path);

    public static List<string> FixTraverse(this GrantServiceTestBundle service, string sid, string path)
        => service.Traverse.FixTraverse(sid, path);

    public static GrantApplyResult FixTraverseAcl(this GrantServiceTestBundle service, string sid, string path)
        => service.Traverse.FixTraverseAcl(sid, path);

    public static GrantRightsState ReadGrantState(this GrantServiceTestBundle service, string path, string sid, IReadOnlyList<string> groupSids)
        => service.Inspection.ReadGrantState(path, sid, groupSids);

    public static PathAclStatus CheckGrantStatus(this GrantServiceTestBundle service, string path, string sid, bool isDeny)
        => service.Inspection.CheckGrantStatus(path, sid, isDeny);

    public static GrantIntentRestoreSnapshot CaptureGrantRestoreSnapshot(this GrantServiceTestBundle service, string sid, string path, bool isDeny)
        => service.Snapshots.CaptureGrantRestoreSnapshot(sid, path, isDeny);

    public static GrantIntentRestoreSnapshot CaptureTraverseRestoreSnapshot(this GrantServiceTestBundle service, string sid, string path)
        => service.Snapshots.CaptureTraverseRestoreSnapshot(sid, path);

    public static bool UpdateFromPath(this GrantServiceTestBundle service, string path, string? sid = null)
        => service.Sync.UpdateFromPath(path, sid);

    public static GrantApplyResult RemoveAll(this GrantServiceTestBundle service, string sid)
        => service.AccountCleanup.RemoveAll(sid);

    public static GrantApplyResult UntrackAll(this GrantServiceTestBundle service, string sid)
        => service.AccountCleanup.UntrackAll(sid);

    public static void ValidateGrant(this GrantServiceTestBundle service, string sid, string path, bool isDeny)
        => service.FileSystemOperations.ValidateGrant(sid, path, isDeny);

    public static void ChangeOwner(this GrantServiceTestBundle service, string path, string sid, bool recursive)
        => service.FileSystemOperations.ChangeOwner(path, sid, recursive);

    public static void ResetOwner(this GrantServiceTestBundle service, string path, bool recursive)
        => service.FileSystemOperations.ResetOwner(path, recursive);
}
