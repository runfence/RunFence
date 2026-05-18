using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Apps.Shortcuts;
using Xunit;

namespace RunFence.IntegrationTests;

internal static class ShortcutPersistenceTestAssertions
{
    public static void AssertShortcut(
        IShortcutComHelper shortcutHelper,
        string shortcutPath,
        string expectedTargetPath,
        string expectedArguments,
        string? expectedWorkingDirectory)
    {
        var definition = shortcutHelper.GetShortcutDefinition(shortcutPath);
        Assert.Equal(expectedTargetPath, definition.TargetPath);
        Assert.Equal(expectedArguments, definition.Arguments);
        Assert.Equal(expectedWorkingDirectory, definition.WorkingDirectory);
    }

    public static FileIdentity ReadFileIdentity(string shortcutPath)
    {
        using var stream = new FileStream(shortcutPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new FileIdentity(
            ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow,
            info.dwVolumeSerialNumber);
    }

    public static bool HasManagedEveryoneDenyAce(string shortcutPath)
    {
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        const FileSystemRights managedDenyRights = FileSystemRights.Delete |
                                                   FileSystemRights.Write |
                                                   FileSystemRights.WriteAttributes |
                                                   FileSystemRights.AppendData;
        var security = new FileInfo(shortcutPath).GetAccessControl(AccessControlSections.Access);
        return security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Any(rule =>
                rule.IdentityReference is SecurityIdentifier sid &&
                sid.Equals(everyoneSid) &&
                rule.AccessControlType == AccessControlType.Deny &&
                (rule.FileSystemRights & managedDenyRights) == managedDenyRights);
    }

    public static void TryDeleteShortcut(IShortcutFilePersistenceNative native, string shortcutPath)
    {
        try
        {
            if (File.Exists(shortcutPath))
                native.DeleteExistingDestination(shortcutPath);
        }
        catch
        {
        }
    }

    public static bool HasAccessDeniedCause(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException)
                return true;

            if (current is Win32Exception win32Exception && win32Exception.NativeErrorCode == 5)
                return true;
        }

        return false;
    }

    internal sealed record FileIdentity(ulong FileIndex, uint VolumeSerialNumber);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        Microsoft.Win32.SafeHandles.SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);
}
