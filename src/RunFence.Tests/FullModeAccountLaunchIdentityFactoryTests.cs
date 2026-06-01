using Moq;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public class FullModeAccountLaunchIdentityFactoryTests
{
    private const string TestSid = "S-1-5-21-100-200-300-1001";

    [Fact]
    public void Create_NormalMode_LeavesPrivilegeUnset()
    {
        var groupQuery = new Mock<ILocalGroupQueryService>(MockBehavior.Strict);
        var factory = new FullModeAccountLaunchIdentityFactory(groupQuery.Object);

        var identity = factory.Create(TestSid, fullMode: false);

        Assert.Equal(TestSid, identity.Sid);
        Assert.Null(identity.PrivilegeLevel);
        groupQuery.VerifyNoOtherCalls();
    }

    [Fact]
    public void Create_FullModeForAdministrator_UsesHighestAllowed()
    {
        var groupQuery = new Mock<ILocalGroupQueryService>();
        groupQuery.Setup(service => service.GetGroupsForUser(TestSid))
            .Returns([new LocalUserAccount("Administrators", "S-1-5-32-544")]);
        var factory = new FullModeAccountLaunchIdentityFactory(groupQuery.Object);

        var identity = factory.Create(TestSid, fullMode: true);

        Assert.Equal(PrivilegeLevel.HighestAllowed, identity.PrivilegeLevel);
    }

    [Fact]
    public void Create_FullModeForStandardAccount_UsesBasic()
    {
        var groupQuery = new Mock<ILocalGroupQueryService>();
        groupQuery.Setup(service => service.GetGroupsForUser(TestSid))
            .Returns([new LocalUserAccount("Users", "S-1-5-32-545")]);
        var factory = new FullModeAccountLaunchIdentityFactory(groupQuery.Object);

        var identity = factory.Create(TestSid, fullMode: true);

        Assert.Equal(PrivilegeLevel.Basic, identity.PrivilegeLevel);
    }
}
