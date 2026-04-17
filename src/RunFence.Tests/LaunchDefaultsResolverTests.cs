using Moq;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Container;
using Xunit;

namespace RunFence.Tests;

public class LaunchDefaultsResolverTests
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly AppDatabase _database = new();
    private readonly LaunchDefaultsResolver _resolver;

    public LaunchDefaultsResolverTests()
    {
        _sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
        {
            Database = _database
        });

        _resolver = new LaunchDefaultsResolver(_sessionProvider.Object);
    }

    [Fact]
    public void ResolveDefaults_NullPrivilegeLevel_FilledFromAccountEntry()
    {
        // Arrange — AccountEntry has HighestAllowed; identity.PrivilegeLevel=null
        _database.Accounts.Add(new AccountEntry { Sid = TestSid, PrivilegeLevel = PrivilegeLevel.HighestAllowed });
        var identity = new AccountLaunchIdentity(TestSid) { PrivilegeLevel = null };

        // Act
        var result = _resolver.ResolveDefaults(identity);

        // Assert — PrivilegeLevel filled from account entry; HighestAllowed → IsUnelevated=false
        var account = Assert.IsType<AccountLaunchIdentity>(result);
        Assert.Equal(PrivilegeLevel.HighestAllowed, account.PrivilegeLevel);
        Assert.False(account.IsUnelevated);
    }

    [Fact]
    public void ResolveDefaults_ExistingPrivilegeLevel_Preserved()
    {
        // Arrange — identity already has PrivilegeLevel.Basic; account entry should not override it
        _database.Accounts.Add(new AccountEntry { Sid = TestSid, PrivilegeLevel = PrivilegeLevel.HighestAllowed });
        var identity = new AccountLaunchIdentity(TestSid) { PrivilegeLevel = PrivilegeLevel.Basic };

        // Act
        var result = _resolver.ResolveDefaults(identity);

        // Assert — original PrivilegeLevel preserved; Basic → IsUnelevated=true
        var account = Assert.IsType<AccountLaunchIdentity>(result);
        Assert.Equal(PrivilegeLevel.Basic, account.PrivilegeLevel);
        Assert.True(account.IsUnelevated);
    }

    [Fact]
    public void ResolveDefaults_NoAccountEntry_DefaultsToBasic()
    {
        // Arrange — no account entry in database; identity.PrivilegeLevel=null
        var identity = new AccountLaunchIdentity(TestSid) { PrivilegeLevel = null };

        // Act
        var result = _resolver.ResolveDefaults(identity);

        // Assert — falls back to PrivilegeLevel.Basic; Basic → IsUnelevated=true
        var account = Assert.IsType<AccountLaunchIdentity>(result);
        Assert.Equal(PrivilegeLevel.Basic, account.PrivilegeLevel);
        Assert.True(account.IsUnelevated);
    }

    [Fact]
    public void ResolveDefaults_AppContainerIdentity_Unchanged()
    {
        // Arrange — AppContainerLaunchIdentity is always IsUnelevated=true; resolver returns it unchanged
        var entry = new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = "S-1-15-2-1" };
        var identity = new AppContainerLaunchIdentity(entry);

        // Act
        var result = _resolver.ResolveDefaults(identity);

        // Assert — same instance returned; IsUnelevated=true
        Assert.Same(identity, result);
        Assert.True(result.IsUnelevated);
    }
}
