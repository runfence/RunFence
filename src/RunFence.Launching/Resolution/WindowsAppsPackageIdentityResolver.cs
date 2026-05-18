using System.Text;
using RunFence.Launching.Windows;

namespace RunFence.Launching.Resolution;

public sealed class WindowsAppsPackageIdentityResolver(IWindowsAppsAliasPathResolver aliasPathResolver)
    : IWindowsAppsPackageIdentityResolver
{
    private const int AppExecLinkStringListOffset = 12;

    public bool TryResolvePackageFamilyName(string exePath, out string packageFamilyName)
        => TryResolvePackageFamilyName(
            exePath,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            out packageFamilyName);

    private bool TryResolvePackageFamilyName(
        string exePath,
        HashSet<string> visitedPaths,
        out string packageFamilyName)
    {
        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(exePath);
        }
        catch
        {
            packageFamilyName = string.Empty;
            return false;
        }

        if (!visitedPaths.Add(normalizedPath))
        {
            packageFamilyName = string.Empty;
            return false;
        }

        if (WindowsAppsPackagePathParser.TryParsePackagePath(normalizedPath, out var directPackagePath))
        {
            packageFamilyName = directPackagePath.PackageFamilyName;
            return true;
        }

        if (aliasPathResolver.IsWindowsAppsAliasPath(normalizedPath)
            && TryReadAppExecLinkStrings(normalizedPath, out var strings)
            && TryResolvePackageFamilyNameFromAlias(strings, visitedPaths, out packageFamilyName))
        {
            return true;
        }

        packageFamilyName = string.Empty;
        return false;
    }

    private static bool TryReadAppExecLinkStrings(string aliasPath, out IReadOnlyList<string> strings)
    {
        strings = [];
        using var handle = LaunchingNative.CreateFile(
            aliasPath,
            0,
            LaunchingNative.FileShareRead | LaunchingNative.FileShareWrite,
            IntPtr.Zero,
            LaunchingNative.OpenExisting,
            LaunchingNative.FileFlagOpenReparsePoint | LaunchingNative.FileFlagBackupSemantics,
            IntPtr.Zero);

        if (handle.IsInvalid)
            return false;

        var buffer = new byte[LaunchingNative.MaximumReparseDataBufferSize];
        if (!LaunchingNative.DeviceIoControl(
                handle,
                LaunchingNative.FsctlGetReparsePoint,
                IntPtr.Zero,
                0,
                buffer,
                (uint)buffer.Length,
                out var bytesReturned,
                IntPtr.Zero))
        {
            return false;
        }

        if (bytesReturned <= AppExecLinkStringListOffset
            || BitConverter.ToUInt32(buffer, 0) != LaunchingNative.IoReparseTagAppExecLink)
        {
            return false;
        }

        var byteCount = (int)bytesReturned - AppExecLinkStringListOffset;
        if (byteCount < 2)
            return false;

        if (byteCount % 2 != 0)
            byteCount--;

        strings = Encoding.Unicode
            .GetString(buffer, AppExecLinkStringListOffset, byteCount)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return strings.Count > 0;
    }

    private bool TryResolvePackageFamilyNameFromAlias(
        IEnumerable<string> strings,
        HashSet<string> visitedPaths,
        out string packageFamilyName)
    {
        foreach (var value in strings)
        {
            if (TryResolvePackageFamilyName(value, visitedPaths, out packageFamilyName))
            {
                return true;
            }
        }

        packageFamilyName = string.Empty;
        return false;
    }
}
