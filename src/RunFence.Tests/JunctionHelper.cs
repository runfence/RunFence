using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RunFence.Tests;

/// <summary>
/// Creates directory junctions (mount points) using native Win32 APIs.
/// Directory junctions require no special privileges — only write access to the target directory.
/// Used by tests that need to verify junction handling behavior without requiring elevation.
/// </summary>
public static class JunctionHelper
{
    /// <summary>
    /// Creates a directory junction at <paramref name="junctionPath"/> pointing to
    /// <paramref name="targetPath"/>.
    /// </summary>
    public static void CreateJunction(string junctionPath, string targetPath)
    {
        // Native path format required for mount-point substitute name
        var nativeTarget = @"\??\" + Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar);
        CreateJunctionWithRawSubstituteName(junctionPath, nativeTarget);
    }

    /// <summary>
    /// Creates a junction directory with an arbitrary raw substitute name, bypassing
    /// the \??\ prefix so tests can inject UNC or other reparse targets directly.
    /// </summary>
    public static void CreateJunctionWithRawSubstituteName(string junctionPath, string substituteName)
    {
        var subNameBytes = Encoding.Unicode.GetBytes(substituteName);

        ushort subLen = (ushort)subNameBytes.Length;
        ushort printOffset = (ushort)(subLen + 2); // substitute name + UTF-16 NUL terminator
        // PathBuffer: substitute name + NUL + empty print name + NUL (all UTF-16)
        var pathBuf = new byte[subLen + 2 + 0 + 2];
        Buffer.BlockCopy(subNameBytes, 0, pathBuf, 0, subLen);

        // ReparseDataLength covers the four ushort header fields plus PathBuffer
        ushort reparseDataLen = (ushort)(8 + pathBuf.Length);

        // Full REPARSE_DATA_BUFFER: tag(4) + reparseDataLen(2) + reserved(2) + header(8) + pathBuf
        var buf = new byte[4 + 2 + 2 + reparseDataLen];
        int i = 0;
        BitConverter.GetBytes(0xA0000003u).CopyTo(buf, i);
        i += 4; // IO_REPARSE_TAG_MOUNT_POINT
        BitConverter.GetBytes(reparseDataLen).CopyTo(buf, i);
        i += 2;
        i += 2; // Reserved
        BitConverter.GetBytes((ushort)0).CopyTo(buf, i);
        i += 2; // SubstituteNameOffset
        BitConverter.GetBytes(subLen).CopyTo(buf, i);
        i += 2; // SubstituteNameLength
        BitConverter.GetBytes(printOffset).CopyTo(buf, i);
        i += 2; // PrintNameOffset
        BitConverter.GetBytes((ushort)0).CopyTo(buf, i);
        i += 2; // PrintNameLength
        pathBuf.CopyTo(buf, i);

        Directory.CreateDirectory(junctionPath);

        using var handle = CreateFileW(junctionPath,
            0x40000000u, // GENERIC_WRITE
            0x1u | 0x2u | 0x4u, // FILE_SHARE_READ|WRITE|DELETE
            IntPtr.Zero, 3u, // OPEN_EXISTING
            0x02000000u, // FILE_FLAG_BACKUP_SEMANTICS
            IntPtr.Zero);

        if (handle.IsInvalid)
            throw new IOException($"CreateFile failed: {Marshal.GetLastWin32Error()}");

        if (!DeviceIoControl(handle, 0x000900A4u, buf, buf.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new IOException($"FSCTL_SET_REPARSE_POINT failed: {Marshal.GetLastWin32Error()}");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        byte[] lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);
}
