namespace RunFence.Acl;

/// <summary>
/// Low-level NTFS owner operations for grant-managed paths.
/// </summary>
public interface IFileOwnerService
{
    void ChangeOwner(string path, string sid, bool recursive);
    void ResetOwner(string path, bool recursive);
}
