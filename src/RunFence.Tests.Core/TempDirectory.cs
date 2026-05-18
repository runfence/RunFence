using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Tests;

/// <summary>
/// Provides an isolated temporary directory for tests.
/// Creates a GUID-suffixed directory on construction and deletes it on disposal.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory(string prefix = "RunFence_Test")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (!Directory.Exists(Path))
            return;

        try
        {
            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser == null)
            {
                Trace.WriteLine($"TempDirectory.Dispose cleanup skipped for '{Path}': current user SID is unavailable.");
                return;
            }

            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(Path);

            while (pendingDirectories.Count > 0)
            {
                var directoryPath = pendingDirectories.Pop();
                GrantCurrentUserFullControl(directoryPath, isDirectory: true, currentUser);

                foreach (var filePath in Directory.GetFiles(directoryPath))
                {
                    GrantCurrentUserFullControl(filePath, isDirectory: false, currentUser);
                    try
                    {
                        File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }

                foreach (var childDirectory in Directory.GetDirectories(directoryPath))
                    pendingDirectories.Push(childDirectory);
            }

            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"TempDirectory.Dispose cleanup failed for '{Path}': {ex}");
        }
    }

    private static void GrantCurrentUserFullControl(string path, bool isDirectory, SecurityIdentifier currentUser)
    {
        if (isDirectory)
        {
            var directory = new DirectoryInfo(path);
            var security = directory.GetAccessControl(AccessControlSections.Access);
            security.SetAccessRuleProtection(true, false);
            foreach (var rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>())
                security.RemoveAccessRuleAll(rule);
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            directory.SetAccessControl(security);
            return;
        }

        var file = new FileInfo(path);
        var fileSecurity = file.GetAccessControl(AccessControlSections.Access);
        fileSecurity.SetAccessRuleProtection(true, false);
        foreach (var rule in fileSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>())
            fileSecurity.RemoveAccessRuleAll(rule);
        fileSecurity.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        file.SetAccessControl(fileSecurity);
    }
}
