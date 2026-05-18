using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RunFence.Infrastructure;

public sealed class BackupIntentFileSystem : IBackupIntentFileSystem
{
    public bool FileExists(string path)
    {
        using var handle = TryOpenWithBackupIntent(path, directory: false);
        return handle is { IsInvalid: false } || File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        using var handle = TryOpenWithBackupIntent(path, directory: true);
        return handle is { IsInvalid: false } || Directory.Exists(path);
    }

    public IReadOnlyList<string> EnumerateDirectories(string path)
    {
        var directories = EnumerateDirectoriesWithBackupIntent(path);
        if (directories != null)
            return directories;

        try
        {
            return Directory.EnumerateDirectories(path).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private IReadOnlyList<string>? EnumerateDirectoriesWithBackupIntent(string path)
    {
        using var handle = TryOpenWithBackupIntent(path, directory: true);
        if (handle is not { IsInvalid: false })
            return null;

        var buffer = Marshal.AllocHGlobal((int)BackupIntentFileSystemNative.DirectoryQueryBufferSize);
        try
        {
            var result = new List<string>();
            var restartScan = true;

            while (true)
            {
                var status = BackupIntentFileSystemNative.NtQueryDirectoryFile(
                    handle,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    out var ioStatus,
                    buffer,
                    BackupIntentFileSystemNative.DirectoryQueryBufferSize,
                    BackupIntentFileSystemNative.FileDirectoryInformation,
                    false,
                    IntPtr.Zero,
                    restartScan);
                restartScan = false;

                if (status == BackupIntentFileSystemNative.StatusNoMoreFiles)
                    break;

                if (status < 0)
                    return null;

                if (ioStatus.Information == IntPtr.Zero)
                    break;

                ReadDirectoryEntries(path, buffer, result);
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void ReadDirectoryEntries(string rootPath, IntPtr buffer, List<string> result)
    {
        var offset = 0;
        while (true)
        {
            var current = IntPtr.Add(buffer, offset);
            var nextOffset = Marshal.ReadInt32(current, BackupIntentFileSystemNative.NextEntryOffsetOffset);
            var attributes = (FileAttributes)Marshal.ReadInt32(current, BackupIntentFileSystemNative.FileAttributesOffset);
            var fileNameLength = Marshal.ReadInt32(current, BackupIntentFileSystemNative.FileNameLengthOffset);
            var fileName = Marshal.PtrToStringUni(
                IntPtr.Add(current, BackupIntentFileSystemNative.FileNameOffset),
                fileNameLength / sizeof(char));

            if ((attributes & FileAttributes.Directory) != 0
                && fileName is not null
                && fileName != "."
                && fileName != "..")
            {
                result.Add(Path.Combine(rootPath, fileName));
            }

            if (nextOffset == 0)
                break;

            offset += nextOffset;
        }
    }

    private SafeFileHandle? TryOpenWithBackupIntent(string path, bool directory)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }

        using var objectName = new NativeObjectName(BuildNtPath(fullPath));
        var objectAttributes = new BackupIntentFileSystemNative.ObjectAttributes
        {
            Length = Marshal.SizeOf<BackupIntentFileSystemNative.ObjectAttributes>(),
            RootDirectory = IntPtr.Zero,
            ObjectName = objectName.UnicodeStringPointer,
            Attributes = BackupIntentFileSystemNative.ObjCaseInsensitive,
            SecurityDescriptor = IntPtr.Zero,
            SecurityQualityOfService = IntPtr.Zero,
        };

        var desiredAccess = directory
            ? BackupIntentFileSystemNative.FileListDirectory | BackupIntentFileSystemNative.Synchronize
            : BackupIntentFileSystemNative.FileReadAttributes | BackupIntentFileSystemNative.Synchronize;
        var createOptions = BackupIntentFileSystemNative.FileOpenForBackupIntent
                            | BackupIntentFileSystemNative.FileSynchronousIoNonAlert
                            | (directory
                                ? BackupIntentFileSystemNative.FileDirectoryFile
                                : BackupIntentFileSystemNative.FileNonDirectoryFile);

        var status = BackupIntentFileSystemNative.NtCreateFile(
            out var handle,
            desiredAccess,
            ref objectAttributes,
            out _,
            IntPtr.Zero,
            BackupIntentFileSystemNative.FileAttributeNormal,
            BackupIntentFileSystemNative.FileShareRead
            | BackupIntentFileSystemNative.FileShareWrite
            | BackupIntentFileSystemNative.FileShareDelete,
            BackupIntentFileSystemNative.FileOpen,
            createOptions,
            IntPtr.Zero,
            0);

        if (status >= 0)
            return handle;

        handle?.Dispose();
        return null;
    }

    private static string BuildNtPath(string fullPath)
    {
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\??\UNC\" + fullPath[2..];

        return @"\??\" + fullPath;
    }

    private sealed class NativeObjectName : IDisposable
    {
        private readonly IntPtr _buffer;

        public NativeObjectName(string path)
        {
            _buffer = Marshal.StringToHGlobalUni(path);
            UnicodeStringPointer = Marshal.AllocHGlobal(Marshal.SizeOf<BackupIntentFileSystemNative.UnicodeString>());
            var unicodeString = new BackupIntentFileSystemNative.UnicodeString
            {
                Length = checked((ushort)(path.Length * sizeof(char))),
                MaximumLength = checked((ushort)((path.Length + 1) * sizeof(char))),
                Buffer = _buffer,
            };
            Marshal.StructureToPtr(unicodeString, UnicodeStringPointer, false);
        }

        public IntPtr UnicodeStringPointer { get; }

        public void Dispose()
        {
            Marshal.FreeHGlobal(UnicodeStringPointer);
            Marshal.FreeHGlobal(_buffer);
        }
    }

    private static class BackupIntentFileSystemNative
    {
        public const int NextEntryOffsetOffset = 0;
        public const int FileAttributesOffset = 56;
        public const int FileNameLengthOffset = 60;
        public const int FileNameOffset = 64;
        public const uint DirectoryQueryBufferSize = 64 * 1024;
        public const int StatusNoMoreFiles = unchecked((int)0x80000006);
        public const int FileDirectoryInformation = 1;
        public const uint FileListDirectory = 0x0001;
        public const uint FileReadAttributes = 0x0080;
        public const uint Synchronize = 0x00100000;
        public const uint FileShareRead = 0x00000001;
        public const uint FileShareWrite = 0x00000002;
        public const uint FileShareDelete = 0x00000004;
        public const uint FileOpen = 0x00000001;
        public const uint FileDirectoryFile = 0x00000001;
        public const uint FileSynchronousIoNonAlert = 0x00000020;
        public const uint FileNonDirectoryFile = 0x00000040;
        public const uint FileOpenForBackupIntent = 0x00004000;
        public const uint FileAttributeNormal = 0x00000080;
        public const uint ObjCaseInsensitive = 0x00000040;

        [DllImport("ntdll.dll")]
        public static extern int NtCreateFile(
            out SafeFileHandle fileHandle,
            uint desiredAccess,
            ref ObjectAttributes objectAttributes,
            out IoStatusBlock ioStatusBlock,
            IntPtr allocationSize,
            uint fileAttributes,
            uint shareAccess,
            uint createDisposition,
            uint createOptions,
            IntPtr eaBuffer,
            uint eaLength);

        [DllImport("ntdll.dll")]
        public static extern int NtQueryDirectoryFile(
            SafeFileHandle fileHandle,
            IntPtr eventHandle,
            IntPtr apcRoutine,
            IntPtr apcContext,
            out IoStatusBlock ioStatusBlock,
            IntPtr fileInformation,
            uint length,
            int fileInformationClass,
            [MarshalAs(UnmanagedType.U1)] bool returnSingleEntry,
            IntPtr fileName,
            [MarshalAs(UnmanagedType.U1)] bool restartScan);

        [StructLayout(LayoutKind.Sequential)]
        public struct UnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ObjectAttributes
        {
            public int Length;
            public IntPtr RootDirectory;
            public IntPtr ObjectName;
            public uint Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IoStatusBlock
        {
            public IntPtr Status;
            public IntPtr Information;
        }
    }
}
