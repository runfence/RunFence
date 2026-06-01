using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace RunFence.Tests;

public class TempDirectoryTests
{
    [Fact]
    public void Dispose_RemovesJunctionWithoutTouchingTarget()
    {
        var userSid = WindowsIdentity.GetCurrent().User;
        Assert.NotNull(userSid);

        using var target = new TempDirectory("RunFence_TempDirectory_JunctionTarget");
        var externalPath = Path.Combine(target.Path, "External");
        Directory.CreateDirectory(externalPath);
        File.WriteAllText(Path.Combine(externalPath, "target.txt"), "unchanged");

        var denyDirectory = new DirectoryInfo(externalPath).GetAccessControl(AccessControlSections.Access);
        denyDirectory.AddAccessRule(new FileSystemAccessRule(
            userSid,
            FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete,
            InheritanceFlags.ContainerInherit,
            PropagationFlags.None,
            AccessControlType.Deny));
        new DirectoryInfo(externalPath).SetAccessControl(denyDirectory);

        using var container = new TempDirectory("RunFence_TempDirectory_Junction");
        var junction = Path.Combine(container.Path, "junction");
        JunctionHelper.CreateJunction(junction, externalPath);

        try
        {
            container.Dispose();

            var restoredSecurity = new DirectoryInfo(externalPath).GetAccessControl(AccessControlSections.Access);
            var hasDenyRule = restoredSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Any(r =>
                string.Equals(r.IdentityReference.Value, userSid.Value, StringComparison.OrdinalIgnoreCase) &&
                r.AccessControlType == AccessControlType.Deny &&
                r.FileSystemRights.HasFlag(FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete) &&
                r.InheritanceFlags.HasFlag(InheritanceFlags.ContainerInherit));
            Assert.True(hasDenyRule);
        }
        finally
        {
            try
            {
                if (Directory.Exists(junction))
                    Directory.Delete(junction);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Dispose_RemovesFileSymlinkWithoutTouchingTarget()
    {
        var userSid = WindowsIdentity.GetCurrent().User;
        Assert.NotNull(userSid);

        using var target = new TempDirectory("RunFence_TempDirectory_FileSymlinkTarget");
        var targetFile = Path.Combine(target.Path, "target.txt");
        File.WriteAllText(targetFile, "target-data");

        using var container = new TempDirectory("RunFence_TempDirectory_FileSymlinkContainer");
        var symlink = Path.Combine(container.Path, "target-link.txt");

        try
        {
            File.CreateSymbolicLink(symlink, targetFile);
        }
        catch (UnauthorizedAccessException)
        {
            throw Xunit.Sdk.SkipException.ForSkip("File symbolic link creation is unavailable on this host.");
        }
        catch (IOException ex) when ((uint)ex.HResult == 0x80070522u)
        {
            throw Xunit.Sdk.SkipException.ForSkip("File symbolic link creation is unavailable on this host.");
        }
        catch (PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip("File symbolic link creation is unavailable on this host.");
        }

        var denyFile = new FileInfo(targetFile).GetAccessControl(AccessControlSections.Access);
        denyFile.AddAccessRule(new FileSystemAccessRule(
            userSid,
            FileSystemRights.Delete,
            AccessControlType.Deny));
        new FileInfo(targetFile).SetAccessControl(denyFile);

        try
        {
            container.Dispose();

            var restoredSecurity = new FileInfo(targetFile).GetAccessControl(AccessControlSections.Access);
            var hasDenyRule = restoredSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Any(r =>
                string.Equals(r.IdentityReference.Value, userSid.Value, StringComparison.OrdinalIgnoreCase) &&
                r.AccessControlType == AccessControlType.Deny &&
                r.FileSystemRights.HasFlag(FileSystemRights.Delete));
            Assert.True(hasDenyRule);
            Assert.Equal("target-data", File.ReadAllText(targetFile));
        }
        finally
        {
            try
            {
                if (File.Exists(symlink))
                    File.Delete(symlink);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Dispose_RemovesDenyAclsAndDeletesDirectory()
    {
        var userSid = WindowsIdentity.GetCurrent().User;
        Assert.NotNull(userSid);

        using var path = new TempDirectory("RunFence_TempDirectory_DenyAcl");
        var childPath = Path.Combine(path.Path, "child");
        var filePath = Path.Combine(path.Path, "child", "sample.txt");
        Directory.CreateDirectory(childPath);
        File.WriteAllText(filePath, "blocked");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        var denyCurrentUserDir = new DirectoryInfo(childPath).GetAccessControl(AccessControlSections.Access);
        denyCurrentUserDir.AddAccessRule(new FileSystemAccessRule(
            userSid,
            FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Write,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Deny));
        new DirectoryInfo(childPath).SetAccessControl(denyCurrentUserDir);

        var fileSecurity = new FileInfo(filePath).GetAccessControl(AccessControlSections.Access);
        fileSecurity.AddAccessRule(new FileSystemAccessRule(
            userSid,
            FileSystemRights.Write | FileSystemRights.Delete,
            AccessControlType.Deny));
        new FileInfo(filePath).SetAccessControl(fileSecurity);

        path.Dispose();

        Assert.False(Directory.Exists(path.Path));
    }
}
