using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Ipc;
using Xunit;

namespace RunFence.Tests;

public class IpcAuthorizationTests
{
    private readonly ILoggingService _log = new Mock<ILoggingService>().Object;
    private readonly ISidResolver _sidResolver = new Mock<ISidResolver>().Object;
    private IpcCallerAuthorizer CreateAuthorizer() => new IpcCallerAuthorizer(_log, _sidResolver);

    private static AppDatabase DbWithIpcCaller(string sid) => new()
    {
        Accounts = [new AccountEntry { Sid = sid, IsIpcCaller = true }]
    };

    // --- IsCallerAuthorizedGlobal tests ---

    [Theory]
    [InlineData(null)]
    [InlineData(@"DOMAIN\User")]
    public void IsCallerAuthorizedGlobal_EmptyGlobalList_ReturnsTrue(string? caller)
    {
        var db = new AppDatabase();
        Assert.True(CreateAuthorizer().IsCallerAuthorizedGlobal(caller, null, db));
    }

    [Fact]
    public void IsCallerAuthorizedGlobal_MatchingSid_ReturnsTrue()
    {
        var sid = "S-1-5-21-0-0-0-1001";
        Assert.True(CreateAuthorizer().IsCallerAuthorizedGlobal(@"DOMAIN\AllowedUser", sid, DbWithIpcCaller(sid)));
    }

    [Fact]
    public void IsCallerAuthorizedGlobal_NonMatchingSid_ReturnsFalse()
    {
        Assert.False(CreateAuthorizer().IsCallerAuthorizedGlobal(@"DOMAIN\OtherUser", "S-1-5-21-0-0-0-9999",
            DbWithIpcCaller("S-1-5-21-0-0-0-1001")));
    }

    [Fact]
    public void IsCallerAuthorizedGlobal_NullIdentityAndSid_ReturnsFalse()
    {
        Assert.False(CreateAuthorizer().IsCallerAuthorizedGlobal(null, null, DbWithIpcCaller("S-1-5-21-0-0-0-1001")));
    }

    // --- IsCallerAuthorized (per-app) tests ---

    [Theory]
    [InlineData(null)]
    [InlineData(@"DOMAIN\User")]
    public void IsCallerAuthorized_EmptyGlobalList_ReturnsTrue(string? caller)
    {
        var app = new AppEntry { Name = "TestApp" };
        var db = new AppDatabase();
        Assert.True(CreateAuthorizer().IsCallerAuthorized(caller, null, app, db));
    }

    [Fact]
    public void IsCallerAuthorized_NullCallerIdentity_NonEmptyList_ReturnsFalse()
    {
        var app = new AppEntry { Name = "TestApp" };
        Assert.False(CreateAuthorizer().IsCallerAuthorized(null, null, app, DbWithIpcCaller("S-1-5-21-0-0-0-1001")));
    }

    [Fact]
    public void IsCallerAuthorized_PerAppOverride_MatchesByName()
    {
        var sid = "S-1-5-21-0-0-0-1001";
        var app = new AppEntry
        {
            Name = "TestApp",
            AllowedIpcCallers = [sid]
        };
        var db = new AppDatabase
        {
            Accounts = [new AccountEntry { Sid = "S-1-5-21-0-0-0-9999", IsIpcCaller = true }],
            SidNames = { [sid] = "AppUser", ["S-1-5-21-0-0-0-9999"] = "GlobalUser" }
        };

        // SID resolution fails for fake SIDs in tests, so this exercises the name fallback.
        Assert.True(CreateAuthorizer().IsCallerAuthorized(@"DOMAIN\AppUser", null, app, db));
    }

    [Fact]
    public void IsCallerAuthorized_PerAppOverride_GlobalUserBlocked()
    {
        var app = new AppEntry
        {
            Name = "TestApp",
            AllowedIpcCallers = ["S-1-5-21-0-0-0-1001"]
        };
        var db = new AppDatabase
        {
            Accounts = [new AccountEntry { Sid = "S-1-5-21-0-0-0-9999", IsIpcCaller = true }],
            SidNames = { ["S-1-5-21-0-0-0-1001"] = "AppUser", ["S-1-5-21-0-0-0-9999"] = "GlobalUser" }
        };

        Assert.False(CreateAuthorizer().IsCallerAuthorized(@"DOMAIN\GlobalUser", null, app, db));
    }

    [Fact]
    public void IsCallerAuthorized_PerAppEmptyList_BlocksAll()
    {
        var app = new AppEntry
        {
            Name = "TestApp",
            AllowedIpcCallers = []
        };
        var db = new AppDatabase
        {
            Accounts = [new AccountEntry { Sid = "S-1-5-21-0-0-0-1001", IsIpcCaller = true }],
            SidNames = { ["S-1-5-21-0-0-0-1001"] = "User" }
        };

        Assert.False(CreateAuthorizer().IsCallerAuthorized(@"DOMAIN\User", null, app, db));
    }

    [Fact]
    public void IsCallerAuthorized_PerAppNull_InheritsGlobal()
    {
        var app = new AppEntry
        {
            Name = "TestApp",
            AllowedIpcCallers = null
        };
        var db = new AppDatabase
        {
            Accounts = [new AccountEntry { Sid = "S-1-5-21-0-0-0-1001", IsIpcCaller = true }],
            SidNames = { ["S-1-5-21-0-0-0-1001"] = @"DOMAIN\User" }
        };

        Assert.True(CreateAuthorizer().IsCallerAuthorized(@"DOMAIN\User", null, app, db));
    }

    [Fact]
    public void IsCallerAuthorized_UsernameOnlyInAccountName_MatchesDomainUser()
    {
        var app = new AppEntry { Name = "TestApp" };
        var db = new AppDatabase
        {
            Accounts = [new AccountEntry { Sid = "S-1-5-21-0-0-0-1001", IsIpcCaller = true }],
            SidNames = { ["S-1-5-21-0-0-0-1001"] = "Admin" }
        };

        Assert.True(CreateAuthorizer().IsCallerAuthorized(@"WORKGROUP\Admin", null, app, db));
    }

    [Theory]
    [InlineData(@"DOMAIN\User", "S-1-5-21-0-0-0-1001", @"DOMAIN\User", true)]
    [InlineData(@"DOMAIN\User", "S-1-5-21-0-0-0-1001", @"domain\user", true)]
    [InlineData(@"DOMAIN\Admin", "S-1-5-21-0-0-0-1001", "admin", true)]
    [InlineData(@"DOMAIN\User", "S-1-5-21-0-0-0-1001", "Other", false)]
    [InlineData(@"DOMAIN\User", "S-1-5-21-0-0-0-1001", @"DOMAIN\Other", false)]
    public void MatchesCaller_NameFallback_Various(string callerIdentity, string sid, string accountName, bool expected)
    {
        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [sid] = accountName };
        Assert.Equal(expected, CreateAuthorizer().MatchesCaller(callerIdentity, sid, sidNames));
    }

    [Theory]
    [InlineData("S-1-5-21-0-0-0-1001", "S-1-5-21-0-0-0-1001", true)]
    [InlineData("S-1-5-21-0-0-0-1001", "S-1-5-21-0-0-0-9999", false)]
    [InlineData("s-1-5-21-0-0-0-1001", "S-1-5-21-0-0-0-1001", true)]
    public void MatchesCaller_SidBasedMatch_Various(string callerSid, string allowedSid, bool expected)
    {
        Assert.Equal(expected, CreateAuthorizer().MatchesCaller("AnyIdentity", callerSid, allowedSid));
    }

    [Fact]
    public void MatchesCaller_SidMatch_TakesPriorityOverNameMismatch()
    {
        Assert.True(CreateAuthorizer().MatchesCaller(@"DOMAIN\Caller", "S-1-5-21-0-0-0-1001", "S-1-5-21-0-0-0-1001"));
    }

    [Fact]
    public void IsCallerAuthorized_WithCallerSid_MatchesBySid()
    {
        var sid = "S-1-5-21-0-0-0-1001";
        var app = new AppEntry { Name = "TestApp" };
        Assert.True(CreateAuthorizer().IsCallerAuthorized(@"DOMAIN\Unknown", sid, app, DbWithIpcCaller(sid)));
    }

    [Fact]
    public void IsCallerAuthorized_WithCallerSid_NonMatching_Denied()
    {
        var app = new AppEntry { Name = "TestApp" };
        Assert.False(CreateAuthorizer().IsCallerAuthorized(@"DOMAIN\User", "S-1-5-21-0-0-0-9999", app,
            DbWithIpcCaller("S-1-5-21-0-0-0-1001")));
    }

    [Fact]
    public void IsCallerAuthorized_GlobalListMatches_ReturnsTrue()
    {
        var app = new AppEntry { Name = "TestApp" };
        var db = new AppDatabase
        {
            Accounts = [new AccountEntry { Sid = "S-1-5-21-0-0-0-1001", IsIpcCaller = true }],
            SidNames = { ["S-1-5-21-0-0-0-1001"] = @"DOMAIN\User" }
        };

        Assert.True(CreateAuthorizer().IsCallerAuthorized(@"DOMAIN\User", null, app, db));
    }

    [Fact]
    public void IsCallerAuthorized_GlobalListDoesNotMatch_ReturnsFalse()
    {
        var app = new AppEntry { Name = "TestApp" };
        var db = new AppDatabase
        {
            Accounts = [new AccountEntry { Sid = "S-1-5-21-0-0-0-1001", IsIpcCaller = true }],
            SidNames = { ["S-1-5-21-0-0-0-1001"] = @"DOMAIN\AllowedUser" }
        };

        Assert.False(CreateAuthorizer().IsCallerAuthorized(@"DOMAIN\BlockedUser", null, app, db));
    }

    // --- IsCallerAuthorizedForAssociation tests ---

    [Theory]
    [InlineData(null)]
    [InlineData(@"DOMAIN\User")]
    public void IsCallerAuthorizedForAssociation_EmptyGlobalList_ReturnsTrue(string? caller)
    {
        // Global empty list = unrestricted (same semantic as IsCallerAuthorized)
        var app = new AppEntry { Name = "TestApp" };
        var db = new AppDatabase();
        Assert.True(CreateAuthorizer().IsCallerAuthorizedForAssociation(caller, null, app, db));
    }

    [Fact]
    public void IsCallerAuthorizedForAssociation_MatchingSid_ReturnsTrue()
    {
        var sid = "S-1-5-21-0-0-0-1001";
        var app = new AppEntry { Name = "TestApp", AllowedIpcCallers = [sid] };
        Assert.True(CreateAuthorizer().IsCallerAuthorizedForAssociation(
            @"DOMAIN\AllowedUser", sid, app, new AppDatabase()));
    }

    [Fact]
    public void IsCallerAuthorizedForAssociation_PerAppEmptyList_UnknownCallerReturnsFalse()
    {
        // Per-app empty list explicitly blocks all callers.
        // (In production the interactive user SID is added, but it's null in the test environment.)
        var app = new AppEntry { Name = "TestApp", AllowedIpcCallers = [] };
        Assert.False(CreateAuthorizer().IsCallerAuthorizedForAssociation(
            @"DOMAIN\UnknownUser", "S-1-5-21-0-0-0-999", app, new AppDatabase()));
    }

    [Fact]
    public void IsCallerAuthorizedForAssociation_InheritsGlobalList()
    {
        var sid = "S-1-5-21-0-0-0-1001";
        var db = DbWithIpcCaller(sid);
        var app = new AppEntry { Name = "TestApp" }; // no per-app override → inherits global
        Assert.True(CreateAuthorizer().IsCallerAuthorizedForAssociation(
            @"DOMAIN\AllowedUser", sid, app, db));
    }

    [Fact]
    public void IsCallerAuthorizedForAssociation_GlobalListNonMatchingSid_ReturnsFalse()
    {
        var db = DbWithIpcCaller("S-1-5-21-0-0-0-1001");
        var app = new AppEntry { Name = "TestApp" };
        Assert.False(CreateAuthorizer().IsCallerAuthorizedForAssociation(
            @"DOMAIN\OtherUser", "S-1-5-21-0-0-0-9999", app, db));
    }
}