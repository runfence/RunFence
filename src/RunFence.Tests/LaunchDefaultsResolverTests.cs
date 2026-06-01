using RunFence.Core.Models;
using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public class LaunchDefaultsResolverTests
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly AppDatabase _database = new();
    private readonly LaunchDefaultsResolver _resolver = new();

    [Fact]
    public void ResolveDefaults_NullPrivilegeLevel_FilledFromAccountEntry()
    {
        // Arrange — AccountEntry has HighestAllowed; identity.PrivilegeLevel=null
        _database.Accounts.Add(new AccountEntry { Sid = TestSid, PrivilegeLevel = PrivilegeLevel.HighestAllowed });
        var identity = new AccountLaunchIdentity(TestSid) { PrivilegeLevel = null };

        // Act
        var result = _resolver.ResolveDefaults(identity, _database);

        // Assert — PrivilegeLevel filled from account entry; HighestAllowed → IsUnelevated=false
        var account = Assert.IsType<AccountLaunchIdentity>(result);
        Assert.Equal(PrivilegeLevel.HighestAllowed, account.PrivilegeLevel);
        Assert.False(account.IsUnelevated);
    }

    [Fact]
    public void ResolveDefaults_ExistingPrivilegeLevel_Preserved()
    {
        // Arrange — identity already has PrivilegeLevel.Isolated; account entry should not override it
        _database.Accounts.Add(new AccountEntry { Sid = TestSid, PrivilegeLevel = PrivilegeLevel.HighestAllowed });
        var identity = new AccountLaunchIdentity(TestSid) { PrivilegeLevel = PrivilegeLevel.Isolated };

        // Act
        var result = _resolver.ResolveDefaults(identity, _database);

        // Assert — original PrivilegeLevel preserved; Isolated → IsUnelevated=true
        var account = Assert.IsType<AccountLaunchIdentity>(result);
        Assert.Equal(PrivilegeLevel.Isolated, account.PrivilegeLevel);
        Assert.True(account.IsUnelevated);
    }

    [Fact]
    public void ResolveDefaults_NoAccountEntry_DefaultsToBasic()
    {
        // Arrange — no account entry in database; identity.PrivilegeLevel=null
        var identity = new AccountLaunchIdentity(TestSid) { PrivilegeLevel = null };

        // Act
        var result = _resolver.ResolveDefaults(identity, _database);

        // Assert — falls back to PrivilegeLevel.Isolated; Isolated → IsUnelevated=true
        var account = Assert.IsType<AccountLaunchIdentity>(result);
        Assert.Equal(PrivilegeLevel.Isolated, account.PrivilegeLevel);
    }

    [Fact]
    public void ResolveDefaults_UsesPassedSnapshot_NotLiveDatabase()
    {
        var snapshot = new AppDatabase();
        snapshot.Accounts.Add(new AccountEntry { Sid = TestSid, PrivilegeLevel = PrivilegeLevel.HighestAllowed });
        var result = _resolver.ResolveDefaults(new AccountLaunchIdentity(TestSid) { PrivilegeLevel = null }, snapshot);

        var account = Assert.IsType<AccountLaunchIdentity>(result);
        Assert.Equal(PrivilegeLevel.HighestAllowed, account.PrivilegeLevel);
    }

    [Fact]
    public void ResolveDefaults_HighIntegrity_RemainsUnelevated()
    {
        var identity = new AccountLaunchIdentity(TestSid) { PrivilegeLevel = PrivilegeLevel.HighIntegrity };

        var result = _resolver.ResolveDefaults(identity, _database);

        var account = Assert.IsType<AccountLaunchIdentity>(result);
        Assert.Equal(PrivilegeLevel.HighIntegrity, account.PrivilegeLevel);
        Assert.True(account.IsUnelevated);
    }
}
