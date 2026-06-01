using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using Xunit;

namespace RunFence.Tests;

public class SpecificContainerAceConflictDetectorTests
{
    [Fact]
    public void HasExplicitSpecificContainerAce_ExplicitPackageSid_ReturnsTrue()
    {
        const string path = @"C:\Target";
        var aclAccessor = new Mock<IPathSecurityDescriptorAccessor>();
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier("S-1-15-2-123-456-789-100"),
            FileSystemRights.ReadAndExecute,
            AccessControlType.Allow));
        aclAccessor.Setup(a => a.PathExists(path, out It.Ref<bool>.IsAny)).Returns(true);
        aclAccessor.Setup(a => a.GetSecurity(path)).Returns(security);

        var detector = new SpecificContainerAceConflictDetector(aclAccessor.Object);

        Assert.True(detector.HasExplicitSpecificContainerAce(path));
    }

    [Fact]
    public void HasExplicitSpecificContainerAce_AllApplicationPackages_ReturnsFalse()
    {
        const string path = @"C:\Target";
        var aclAccessor = new Mock<IPathSecurityDescriptorAccessor>();
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(AclHelper.AllApplicationPackagesSid),
            FileSystemRights.ReadAndExecute,
            AccessControlType.Allow));
        aclAccessor.Setup(a => a.PathExists(path, out It.Ref<bool>.IsAny)).Returns(true);
        aclAccessor.Setup(a => a.GetSecurity(path)).Returns(security);

        var detector = new SpecificContainerAceConflictDetector(aclAccessor.Object);

        Assert.False(detector.HasExplicitSpecificContainerAce(path));
    }

    [Fact]
    public void HasLowIntegrityAce_ExplicitLowIntegritySid_ReturnsTrue()
    {
        const string path = @"C:\Target";
        var aclAccessor = new Mock<IPathSecurityDescriptorAccessor>();
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(AclHelper.LowIntegritySid),
            FileSystemRights.ReadAndExecute,
            AccessControlType.Allow));
        aclAccessor.Setup(a => a.PathExists(path, out It.Ref<bool>.IsAny)).Returns(true);
        aclAccessor.Setup(a => a.GetSecurity(path)).Returns(security);

        var detector = new SpecificContainerAceConflictDetector(aclAccessor.Object);

        Assert.True(detector.HasLowIntegrityAce(path));
    }
}
