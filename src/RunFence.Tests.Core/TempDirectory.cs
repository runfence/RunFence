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
            var root = NormalizePath(Path);
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
                if (!IsUnderTempRoot(directoryPath, root))
                    continue;

                GrantCurrentUserFullControl(directoryPath, isDirectory: true, currentUser);

                foreach (var filePath in Directory.GetFiles(directoryPath))
                {
                    if (!IsUnderTempRoot(filePath, root))
                        continue;

                    if (!IsReparsePoint(filePath))
                        GrantCurrentUserFullControl(filePath, isDirectory: false, currentUser);

                    try
                    {
                        File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);
                    }
                    catch (Exception)
                    {
                    }

                    try
                    {
                        File.Delete(filePath);
                    }
                    catch (Exception)
                    {
                    }
                }

                foreach (var childDirectory in Directory.GetDirectories(directoryPath))
                {
                    if (!IsUnderTempRoot(childDirectory, root))
                        continue;

                    if (IsReparsePoint(childDirectory))
                    {
                        try
                        {
                            File.SetAttributes(childDirectory, File.GetAttributes(childDirectory) & ~FileAttributes.ReadOnly);
                        }
                        catch (Exception)
                        {
                        }

                        try
                        {
                            Directory.Delete(childDirectory);
                        }
                        catch (Exception)
                        {
                        }

                        continue;
                    }

                    pendingDirectories.Push(childDirectory);
                }
            }

            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"TempDirectory.Dispose cleanup failed for '{Path}': {ex}");
        }
    }

    private string NormalizePath(string path)
        => global::System.IO.Path.GetFullPath(path).TrimEnd(global::System.IO.Path.DirectorySeparatorChar, global::System.IO.Path.AltDirectorySeparatorChar) + global::System.IO.Path.DirectorySeparatorChar;

    private bool IsUnderTempRoot(string candidatePath, string root)
        => NormalizePath(candidatePath).StartsWith(root, StringComparison.OrdinalIgnoreCase);

    private bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private void GrantCurrentUserFullControl(string path, bool isDirectory, SecurityIdentifier currentUser)
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
