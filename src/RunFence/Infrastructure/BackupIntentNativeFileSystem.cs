using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;

namespace RunFence.Infrastructure;

public sealed class BackupIntentNativeFileSystem : IBackupIntentNativeFileSystem
{
    public BackupIntentNativeOpenResult TryOpen(string path, bool directory)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return new BackupIntentNativeOpenResult(null, BackupIntentNativeStatus.StatusObjectPathInvalid);
        }

        try
        {
            using var objectName = new NativeObjectName(BuildNtPath(fullPath));
            var objectAttributes = new NativeMethods.ObjectAttributes
            {
                Length = Marshal.SizeOf<NativeMethods.ObjectAttributes>(),
                RootDirectory = IntPtr.Zero,
                ObjectName = objectName.UnicodeStringPointer,
                Attributes = NativeMethods.ObjCaseInsensitive,
                SecurityDescriptor = IntPtr.Zero,
                SecurityQualityOfService = IntPtr.Zero,
            };

            var desiredAccess = directory
                ? NativeMethods.FileListDirectory | NativeMethods.Synchronize
                : NativeMethods.FileReadAttributes | NativeMethods.Synchronize;
            var createOptions = NativeMethods.FileOpenForBackupIntent
                                | NativeMethods.FileSynchronousIoNonAlert
                                | (directory
                                    ? NativeMethods.FileDirectoryFile
                                    : NativeMethods.FileNonDirectoryFile);

            var status = NativeMethods.NtCreateFile(
                out var handle,
                desiredAccess,
                ref objectAttributes,
                out _,
                IntPtr.Zero,
                NativeMethods.FileAttributeNormal,
                NativeMethods.FileShareRead | NativeMethods.FileShareWrite | NativeMethods.FileShareDelete,
                NativeMethods.FileOpen,
                createOptions,
                IntPtr.Zero,
                0);

            if (status >= 0)
                return new BackupIntentNativeOpenResult(handle, status);

            handle?.Dispose();
            return new BackupIntentNativeOpenResult(null, status);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or OverflowException)
        {
            return new BackupIntentNativeOpenResult(null, BackupIntentNativeStatus.StatusObjectPathInvalid);
        }
    }

    public bool TryEnumerateDirectories(SafeFileHandle handle, string rootPath, out IReadOnlyList<string> directories)
    {
        directories = [];

        var buffer = Marshal.AllocHGlobal((int)NativeMethods.DirectoryQueryBufferSize);
        try
        {
            var result = new List<string>();
            var restartScan = true;

            while (true)
            {
                var status = NativeMethods.NtQueryDirectoryFile(
                    handle,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    out var ioStatus,
                    buffer,
                    NativeMethods.DirectoryQueryBufferSize,
                    NativeMethods.FileDirectoryInformation,
                    false,
                    IntPtr.Zero,
                    restartScan);
                restartScan = false;

                if (status == BackupIntentNativeStatus.StatusNoMoreFiles)
                    break;

                if (status < 0)
                    return false;

                if (ioStatus.Information == IntPtr.Zero)
                    break;

                ReadDirectoryEntries(rootPath, buffer, result);
            }

            directories = result;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public bool TryGetLastWriteTimeUtc(SafeFileHandle handle, out DateTime lastWriteTimeUtc)
    {
        lastWriteTimeUtc = default;

        if (!NativeMethods.GetFileTime(handle, out _, out _, out var writeTime))
            return false;

        lastWriteTimeUtc = DateTime.FromFileTimeUtc((((long)writeTime.HighDateTime) << 32) | (uint)writeTime.LowDateTime);
        return true;
    }

    public SafeFileHandle CreateRelativeDirectory(
        SafeFileHandle parentHandle,
        string childName,
        uint desiredAccess,
        uint shareAccess,
        byte[]? securityDescriptor = null)
    {
        return CreateRelativeObject(
            parentHandle,
            childName,
            desiredAccess,
            shareAccess,
            NativeMethods.FileOpenIf,
            NativeMethods.FileDirectoryFile,
            securityDescriptor);
    }

    public SafeFileHandle CreateRelativeFile(
        SafeFileHandle parentHandle,
        string childName,
        uint desiredAccess,
        uint shareAccess,
        bool overwrite,
        byte[]? securityDescriptor = null)
    {
        return CreateRelativeObject(
            parentHandle,
            childName,
            desiredAccess,
            shareAccess,
            overwrite ? NativeMethods.FileOverwriteIf : NativeMethods.FileCreate,
            NativeMethods.FileNonDirectoryFile,
            securityDescriptor);
    }

    private static void ReadDirectoryEntries(string rootPath, IntPtr buffer, List<string> result)
    {
        var offset = 0;
        while (true)
        {
            var current = IntPtr.Add(buffer, offset);
            var nextOffset = Marshal.ReadInt32(current, NativeMethods.NextEntryOffsetOffset);
            var attributes = (FileAttributes)Marshal.ReadInt32(current, NativeMethods.FileAttributesOffset);
            var fileNameLength = Marshal.ReadInt32(current, NativeMethods.FileNameLengthOffset);
            var fileName = Marshal.PtrToStringUni(
                IntPtr.Add(current, NativeMethods.FileNameOffset),
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

    private static string BuildNtPath(string fullPath)
    {
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\??\UNC\" + fullPath[2..];

        return @"\??\" + fullPath;
    }

    private static SafeFileHandle CreateRelativeObject(
        SafeFileHandle parentHandle,
        string childName,
        uint desiredAccess,
        uint shareAccess,
        uint createDisposition,
        uint kindCreateOption,
        byte[]? securityDescriptor)
    {
        if (string.IsNullOrWhiteSpace(childName))
            throw new ArgumentException("Relative child name must not be empty.", nameof(childName));
        if (childName is "." or "..")
            throw new ArgumentException("Relative child name must not be a current or parent directory segment.", nameof(childName));
        if (childName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            throw new ArgumentException("Relative child name must be a single path segment.", nameof(childName));

        using var objectName = new NativeObjectName(childName);
        GCHandle securityDescriptorHandle = default;
        desiredAccess |= NativeMethods.Synchronize;
        var objectAttributes = new NativeMethods.ObjectAttributes
        {
            Length = Marshal.SizeOf<NativeMethods.ObjectAttributes>(),
            RootDirectory = parentHandle.DangerousGetHandle(),
            ObjectName = objectName.UnicodeStringPointer,
            Attributes = NativeMethods.ObjCaseInsensitive,
            SecurityDescriptor = IntPtr.Zero,
            SecurityQualityOfService = IntPtr.Zero,
        };
        if (securityDescriptor is { Length: > 0 })
        {
            securityDescriptorHandle = GCHandle.Alloc(securityDescriptor, GCHandleType.Pinned);
            objectAttributes.SecurityDescriptor = securityDescriptorHandle.AddrOfPinnedObject();
        }

        try
        {
            var status = NativeMethods.NtCreateFile(
                out var handle,
                desiredAccess,
                ref objectAttributes,
                out _,
                IntPtr.Zero,
                NativeMethods.FileAttributeNormal,
                shareAccess,
                createDisposition,
                NativeMethods.FileOpenForBackupIntent | NativeMethods.FileSynchronousIoNonAlert | kindCreateOption,
                IntPtr.Zero,
                0);

            if (status < 0)
            {
                handle?.Dispose();
                throw new IOException($"NtCreateFile failed for relative child '{childName}' with status 0x{status:X8}.");
            }

            return handle;
        }
        finally
        {
            if (securityDescriptorHandle.IsAllocated)
            {
                securityDescriptorHandle.Free();
            }
        }
    }

    private sealed class NativeObjectName : IDisposable
    {
        private readonly IntPtr _buffer;

        public NativeObjectName(string path)
        {
            var length = checked((ushort)(path.Length * sizeof(char)));
            var maximumLength = checked((ushort)((path.Length + 1) * sizeof(char)));
            var buffer = IntPtr.Zero;
            var unicodeStringPointer = IntPtr.Zero;
            try
            {
                buffer = Marshal.StringToHGlobalUni(path);
                unicodeStringPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.UnicodeString>());
                var unicodeString = new NativeMethods.UnicodeString
                {
                    Length = length,
                    MaximumLength = maximumLength,
                    Buffer = buffer,
                };
                Marshal.StructureToPtr(unicodeString, unicodeStringPointer, false);

                _buffer = buffer;
                UnicodeStringPointer = unicodeStringPointer;
            }
            catch
            {
                if (unicodeStringPointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(unicodeStringPointer);
                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);
                throw;
            }
        }

        public IntPtr UnicodeStringPointer { get; }

        public void Dispose()
        {
            Marshal.FreeHGlobal(UnicodeStringPointer);
            Marshal.FreeHGlobal(_buffer);
        }
    }

    private static class NativeMethods
    {
        public const int NextEntryOffsetOffset = 0;
        public const int FileAttributesOffset = 56;
        public const int FileNameLengthOffset = 60;
        public const int FileNameOffset = 64;
        public const uint DirectoryQueryBufferSize = 64 * 1024;
        public const int FileDirectoryInformation = 1;
        public const uint FileListDirectory = 0x0001;
        public const uint FileReadAttributes = 0x0080;
        public const uint Synchronize = 0x00100000;
        public const uint FileShareRead = 0x00000001;
        public const uint FileShareWrite = 0x00000002;
        public const uint FileShareDelete = 0x00000004;
        public const uint FileOpen = 0x00000001;
        public const uint FileCreate = 0x00000002;
        public const uint FileOpenIf = 0x00000003;
        public const uint FileOverwriteIf = 0x00000005;
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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetFileTime(
            SafeFileHandle hFile,
            out FileTime lpCreationTime,
            out FileTime lpLastAccessTime,
            out FileTime lpLastWriteTime);

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

        [StructLayout(LayoutKind.Sequential)]
        public struct FileTime
        {
            public int LowDateTime;
            public int HighDateTime;
        }
    }
}
