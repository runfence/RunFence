using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace RunFence.Tests;

public class TempDirectoryTests
{
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
