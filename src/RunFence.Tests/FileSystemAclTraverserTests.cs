using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class FileSystemAclTraverserTests
{
    [Fact]
    public void Traverse_ProtectedDirectoryRoot_UsesAclAccessorPathExistsAndYieldsRoot()
    {
        const string root = @"C:\DeniedRoot";
        var log = new Mock<ILoggingService>();
        var aclAccessor = new Mock<IAclAccessor>();
        bool isDirectory = true;
        aclAccessor.Setup(a => a.PathExists(root, out isDirectory)).Returns(true);
        aclAccessor.Setup(a => a.GetSecurity(root)).Returns(new DirectorySecurity());

        var traverser = new FileSystemAclTraverser(log.Object, aclAccessor.Object);

        var entries = traverser.Traverse([root], new Progress<long>(), CancellationToken.None).ToList();

        var entry = Assert.Single(entries);
        Assert.Equal(root, entry.Path);
        Assert.True(entry.IsDirectory);
        aclAccessor.Verify(a => a.PathExists(root, out isDirectory), Times.AtLeastOnce);
    }

    [Fact]
    public void Traverse_FileRoot_UsesResolvedFileStateWithoutDirectoryEnumeration()
    {
        const string root = @"C:\DeniedRoot\tool.exe";
        var log = new Mock<ILoggingService>();
        var aclAccessor = new Mock<IAclAccessor>();
        bool isDirectory = false;
        aclAccessor.Setup(a => a.PathExists(root, out isDirectory)).Returns(true);
        aclAccessor.Setup(a => a.GetSecurity(root)).Returns(new FileSecurity());

        var traverser = new FileSystemAclTraverser(log.Object, aclAccessor.Object);

        var entries = traverser.Traverse([root], new Progress<long>(), CancellationToken.None).ToList();

        var entry = Assert.Single(entries);
        Assert.Equal(root, entry.Path);
        Assert.False(entry.IsDirectory);
    }
}
