using Moq;
using RunFence.Account.UI;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public sealed class TerminalLaunchIdentitySelectorTests
{
    private const string TestSid = "S-1-5-21-100-200-300-1001";

    [Fact]
    public void ResolveLaunchIdentity_SharedWindowsTerminal_KeepsAccountDefault()
    {
        var deploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(Path.GetTempPath()));
        var selector = new TerminalLaunchIdentitySelector(CreateDatabaseProvider(new AppDatabase()), deploymentPaths);

        var result = selector.ResolveLaunchIdentity(new AccountLaunchIdentity(TestSid), deploymentPaths.SharedExecutablePath);

        var accountIdentity = Assert.IsType<AccountLaunchIdentity>(result);
        Assert.Equal(TestSid, accountIdentity.Sid);
        Assert.Null(accountIdentity.PrivilegeLevel);
    }

    [Fact]
    public void ResolveLaunchIdentity_SharedWindowsTerminalVariant_KeepsAccountDefault()
    {
        var deploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(Path.GetTempPath()));
        var selector = new TerminalLaunchIdentitySelector(CreateDatabaseProvider(new AppDatabase()), deploymentPaths);

        var result = selector.ResolveLaunchIdentity(
            new AccountLaunchIdentity(TestSid),
            deploymentPaths.GetSharedExecutablePath(PrivilegeLevel.Isolated));

        var accountIdentity = Assert.IsType<AccountLaunchIdentity>(result);
        Assert.Equal(TestSid, accountIdentity.Sid);
        Assert.Null(accountIdentity.PrivilegeLevel);
    }

    [Fact]
    public void ResolveLaunchIdentity_NativeWindowsTerminalWithoutHighIntegrityDefault_UsesBasic()
    {
        var selector = new TerminalLaunchIdentitySelector(
            CreateDatabaseProvider(new AppDatabase()),
            new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(Path.GetTempPath())));

        var result = selector.ResolveLaunchIdentity(
            new AccountLaunchIdentity(TestSid),
            @"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\wt.exe");

        var accountIdentity = Assert.IsType<AccountLaunchIdentity>(result);
        Assert.Equal(PrivilegeLevel.Basic, accountIdentity.PrivilegeLevel);
    }

    [Fact]
    public void ResolveLaunchIdentity_NativeWindowsTerminalWithHighIntegrityDefault_KeepsAccountDefault()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(TestSid).PrivilegeLevel = PrivilegeLevel.HighIntegrity;
        var selector = new TerminalLaunchIdentitySelector(
            CreateDatabaseProvider(database),
            new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(Path.GetTempPath())));

        var result = selector.ResolveLaunchIdentity(
            new AccountLaunchIdentity(TestSid),
            @"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\wt.exe");

        var accountIdentity = Assert.IsType<AccountLaunchIdentity>(result);
        Assert.Null(accountIdentity.PrivilegeLevel);
    }

    private static IDatabaseProvider CreateDatabaseProvider(AppDatabase database)
    {
        var databaseProvider = new Mock<IDatabaseProvider>();
        databaseProvider.Setup(provider => provider.GetDatabase()).Returns(database);
        return databaseProvider.Object;
    }
}
