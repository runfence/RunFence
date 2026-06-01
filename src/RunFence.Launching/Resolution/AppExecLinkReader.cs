using System.Text;
using RunFence.Launching.Windows;

namespace RunFence.Launching.Resolution;

public sealed class AppExecLinkReader : IAppExecLinkReader
{
    private const int AppExecLinkStringListOffset = 12;

    public bool IsAppExecLink(string path)
    {
        if (!TryReadReparseBuffer(path, out var buffer, out var bytesReturned)
            || bytesReturned < sizeof(uint))
        {
            return false;
        }

        return BitConverter.ToUInt32(buffer, 0) == LaunchingNative.IoReparseTagAppExecLink;
    }

    public bool TryReadStrings(string path, out IReadOnlyList<string> strings)
    {
        strings = [];
        if (!TryReadReparseBuffer(path, out var buffer, out var bytesReturned)
            || bytesReturned <= AppExecLinkStringListOffset
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

    private bool TryReadReparseBuffer(string path, out byte[] buffer, out uint bytesReturned)
    {
        buffer = [];
        bytesReturned = 0;

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

        buffer = new byte[LaunchingNative.MaximumReparseDataBufferSize];
        return LaunchingNative.DeviceIoControl(
            handle,
            LaunchingNative.FsctlGetReparsePoint,
            IntPtr.Zero,
            0,
            buffer,
            (uint)buffer.Length,
            out bytesReturned,
            IntPtr.Zero);
    }
}
