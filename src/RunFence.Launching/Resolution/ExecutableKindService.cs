using RunFence.Launching.Windows;

namespace RunFence.Launching.Resolution;

public sealed class ExecutableKindService(IExecutableFileSystem fileSystem) : IExecutableKindService
{
    private const ushort AppContainerFlag = 0x1000;

    private static readonly HashSet<string> KnownBrowserExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "firefox.exe", "msedge.exe", "brave.exe", "opera.exe", "vivaldi.exe",
        "iexplore.exe", "opera_gx.exe", "thorium.exe", "librewolf.exe", "waterfox.exe",
        "palemoon.exe", "seamonkey.exe", "basilisk.exe",
    };

    public bool IsKnownBrowserExe(string path) => KnownBrowserExeNames.Contains(Path.GetFileName(path));

    public bool IsUwpExeFile(string path)
    {
        try
        {
            if (fileSystem.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                return IsAppExecutionAlias(path);
        }
        catch
        {
            return false;
        }

        return HasAppContainerFlag(path);
    }

    public bool SuggestsAboveBasicPrivilegeLevel(string path) =>
        IsKnownBrowserExe(path) || IsUwpExeFile(path);

    private bool IsAppExecutionAlias(string path)
    {
        using var handle = LaunchingNative.CreateFile(
            path,
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
                out _,
                IntPtr.Zero))
        {
            return false;
        }

        return BitConverter.ToUInt32(buffer, 0) == LaunchingNative.IoReparseTagAppExecLink;
    }

    private bool HasAppContainerFlag(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buf = new byte[512];
            var read = fs.Read(buf, 0, buf.Length);
            if (read < 64 || buf[0] != 'M' || buf[1] != 'Z')
                return false;

            var peOffset = BitConverter.ToInt32(buf, 0x3C);
            if (peOffset < 0 || (long)peOffset + 96 > read)
                return false;

            if (buf[peOffset] != 'P' || buf[peOffset + 1] != 'E'
                || buf[peOffset + 2] != 0 || buf[peOffset + 3] != 0)
            {
                return false;
            }

            var dllChars = BitConverter.ToUInt16(buf, peOffset + 94);
            return (dllChars & AppContainerFlag) != 0;
        }
        catch
        {
            return false;
        }
    }
}
