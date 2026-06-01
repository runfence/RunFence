namespace PrefTrans.Services.IO;

public class TaskbarLegacyOwnershipDetector(TaskbarProfilePathPatcher profilePathPatcher)
{
    public bool IsOwnedByCurrentProfile(byte[]? favorites, byte[]? favoritesResolve, string targetProfile)
    {
        if (string.IsNullOrEmpty(targetProfile))
            return false;

        return profilePathPatcher.ContainsPathUtf16(favorites, targetProfile) ||
               profilePathPatcher.ContainsPathUtf16(favoritesResolve, targetProfile);
    }
}
