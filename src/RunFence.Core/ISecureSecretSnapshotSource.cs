namespace RunFence.Core;

public interface ISecureSecretSnapshotSource
{
    void UseSnapshot(SecureSecretAction action);
    T TransformSnapshot<T>(SecureSecretFunc<T> action);
}
