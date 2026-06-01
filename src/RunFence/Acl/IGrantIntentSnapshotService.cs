namespace RunFence.Acl;

public interface IGrantIntentSnapshotService
{
    GrantIntentRestoreSnapshot CaptureGrantRestoreSnapshot(string sid, string path, bool isDeny);

    GrantIntentRestoreSnapshot CaptureTraverseRestoreSnapshot(string sid, string path);
}
