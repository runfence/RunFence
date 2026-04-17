using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Acl;
using RunFence.SidMigration;
using Xunit;

namespace RunFence.Tests;

public class SidMigrationServiceTests
{
    private readonly SidMigrationService _service;
    private readonly Mock<ISidNameCacheService> _sidNameCache;
    private readonly Mock<ISidAclScanService> _aclScan;
    private AppDatabase _testDatabase = new();

    private const string OldSid1 = "S-1-5-21-9999999999-9999999999-9999999999-1001";
    private const string OldSid2 = "S-1-5-21-9999999999-9999999999-9999999999-1002";
    private const string NewSid1 = "S-1-5-21-1111111111-1111111111-1111111111-1001";
    private const string NewSid2 = "S-1-5-21-1111111111-1111111111-1111111111-1002";

    public SidMigrationServiceTests()
    {
        var sidResolver = new Mock<ISidResolver>();
        _aclScan = new Mock<ISidAclScanService>();
        _sidNameCache = new Mock<ISidNameCacheService>();
        var sidCleanupHelper = new Mock<ISidCleanupHelper>();
        var dbProvider = new LambdaDatabaseProvider(() => _testDatabase);
        var realCleanupHelper = new SidCleanupHelper(dbProvider);
        sidCleanupHelper
            .Setup(h => h.CleanupSidFromAppData(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string sid, bool removeApps) => realCleanupHelper.CleanupSidFromAppData(sid, removeApps));
        _service = new SidMigrationService(sidResolver.Object, sidCleanupHelper.Object, _aclScan.Object, _sidNameCache.Object, dbProvider);
    }

    // --- BuildMappings tests ---

    [Fact]
    public void BuildMappings_NoCredentials_ReturnsEmpty()
    {
        var result = _service.BuildMappings(
            [],
            new List<LocalUserAccount> { new("TestUser", NewSid1) });

        Assert.Empty(result);
    }

    [Fact]
    public void BuildMappings_CurrentAccountExcluded()
    {
        var creds = new List<CredentialEntry>
        {
            new() { Sid = SidResolutionHelper.GetCurrentUserSid() }
        };
        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SidResolutionHelper.GetCurrentUserSid()] = "TestUser"
        };
        var localAccounts = new List<LocalUserAccount> { new("TestUser", NewSid1) };

        var result = _service.BuildMappings(creds, localAccounts, sidNames);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildMappings_NoMatchingLocalAccount_ReturnsEmpty()
    {
        var creds = new List<CredentialEntry>
        {
            new() { Sid = OldSid1 }
        };
        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [OldSid1] = "NonExistentUser"
        };
        var localAccounts = new List<LocalUserAccount> { new("OtherUser", NewSid1) };

        var result = _service.BuildMappings(creds, localAccounts, sidNames);

        Assert.Empty(result);
    }

    // --- MigrateAppData tests ---

    [Fact]
    public void MigrateAppData_UpdatesAllFields()
    {
        var database = new AppDatabase
        {
            Apps = [new() { Id = "a1", AccountSid = OldSid1 }]
        };
        database.GetOrCreateAccount(OldSid1).IsIpcCaller = true;

        var credentialStore = new CredentialStore
        {
            Credentials = [new() { Sid = OldSid1 }]
        };

        var mappings = new List<SidMigrationMapping>
        {
            new(OldSid1, NewSid1, "TestUser")
        };

        _testDatabase = database;
        var counts = _service.MigrateAppData(mappings, credentialStore);

        Assert.Equal(1, counts.Credentials);
        Assert.Equal(1, counts.Apps);
        Assert.Equal(1, counts.IpcCallers);
        Assert.Equal(0, counts.AllowEntries);

        Assert.Equal(NewSid1, database.Apps[0].AccountSid);
        Assert.True(database.GetAccount(NewSid1)?.IsIpcCaller);
        Assert.Equal(NewSid1, credentialStore.Credentials[0].Sid);
    }

    [Fact]
    public void MigrateAppData_PartialMigration_UnmappedUnchanged()
    {
        var database = new AppDatabase
        {
            Apps =
            [
                new() { Id = "a1", AccountSid = OldSid1 },
                new() { Id = "a2", AccountSid = OldSid2 },
                new() { Id = "a3", AccountSid = "S-1-5-21-3333333333-3333333333-3333333333-1003" }
            ]
        };

        var credentialStore = new CredentialStore
        {
            Credentials =
            [
                new() { Sid = OldSid1 },
                new() { Sid = OldSid2 },
                new() { Sid = "S-1-5-21-3333333333-3333333333-3333333333-1003" }
            ]
        };

        var mappings = new List<SidMigrationMapping>
        {
            new(OldSid1, NewSid1, "User1"),
            new(OldSid2, NewSid2, "User2")
        };

        _testDatabase = database;
        var counts = _service.MigrateAppData(mappings, credentialStore);

        Assert.Equal(2, counts.Credentials);
        Assert.Equal(2, counts.Apps);
        Assert.Equal("S-1-5-21-3333333333-3333333333-3333333333-1003", credentialStore.Credentials[2].Sid);
        Assert.Equal("S-1-5-21-3333333333-3333333333-3333333333-1003", database.Apps[2].AccountSid);
    }

    [Fact]
    public void MigrateAppData_EmptyMappings_AllDataUnchanged()
    {
        var database = new AppDatabase
        {
            Apps = [new() { Id = "a1", AccountSid = OldSid1 }]
        };

        var credentialStore = new CredentialStore
        {
            Credentials = [new() { Sid = OldSid1 }]
        };

        _testDatabase = database;
        var counts = _service.MigrateAppData(new List<SidMigrationMapping>(), credentialStore);

        Assert.Equal(0, counts.Credentials);
        Assert.Equal(0, counts.Apps);
        Assert.Equal(OldSid1, credentialStore.Credentials[0].Sid);
    }

    [Fact]
    public void MigrateAppData_AllowedAclEntries_SidReplacement()
    {
        var database = new AppDatabase
        {
            Apps =
            [
                new()
                {
                    Id = "a1",
                    AccountSid = "other-sid",
                    AclMode = AclMode.Allow,
                    AllowedAclEntries = [new AllowAclEntry { Sid = OldSid1, AllowExecute = true, AllowWrite = false }]
                }
            ]
        };

        _testDatabase = database;


        var counts = _service.MigrateAppData([new(OldSid1, NewSid1, "TestUser")], new CredentialStore());

        Assert.Equal(1, counts.AllowEntries);
        var entry = database.Apps[0].AllowedAclEntries![0];
        Assert.Equal(NewSid1, entry.Sid);
        Assert.True(entry.AllowExecute);
        Assert.False(entry.AllowWrite);
    }

    [Fact]
    public void MigrateAppData_AllowedAclEntries_DeduplicatesAndMergesFlags()
    {
        // Two AllowAclEntries with different old SIDs both map to the same new SID.
        // The resulting merged entry must OR the boolean flags from both originals.
        var database = new AppDatabase
        {
            Apps =
            [
                new()
                {
                    Id = "a1",
                    AccountSid = "other-sid",
                    AclMode = AclMode.Allow,
                    AllowedAclEntries =
                    [
                        new AllowAclEntry { Sid = OldSid1, AllowExecute = true, AllowWrite = false },
                        new AllowAclEntry { Sid = OldSid2, AllowExecute = false, AllowWrite = true }
                    ]
                }
            ]
        };

        _testDatabase = database;

        var mappings = new List<SidMigrationMapping>
        {
            new(OldSid1, NewSid1, "User1"),
            new(OldSid2, NewSid1, "User2") // both map to same new SID
        };

        _service.MigrateAppData(mappings, new CredentialStore());

        // After deduplication: single entry with NewSid1 and OR-merged flags
        var entries = database.Apps[0].AllowedAclEntries!;
        Assert.Single(entries);
        Assert.Equal(NewSid1, entries[0].Sid);
        Assert.True(entries[0].AllowExecute, "AllowExecute must be ORed from both entries");
        Assert.True(entries[0].AllowWrite, "AllowWrite must be ORed from both entries");
    }

    [Fact]
    public void MigrateAppData_AllowedIpcCallers_PerApp_SidReplacement()
    {
        var database = new AppDatabase
        {
            Apps =
            [
                new()
                {
                    Id = "a1",
                    AccountSid = "other-sid",
                    AllowedIpcCallers = [OldSid1]
                }
            ]
        };

        _testDatabase = database;


        var counts = _service.MigrateAppData([new(OldSid1, NewSid1, "TestUser")], new CredentialStore());

        Assert.Equal(1, counts.IpcCallers);
        Assert.Equal(NewSid1, database.Apps[0].AllowedIpcCallers![0]);
    }

    [Fact]
    public async Task DiscoverOrphanedSidsAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        // Verify that SidMigrationService correctly propagates cancellation from ISidAclScanService.
        var ct = new CancellationToken(true);
        var progress = new Progress<(long, long)>();

        _aclScan
            .Setup(s => s.DiscoverOrphanedSidsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<IProgress<(long, long)>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<string> _, IProgress<(long, long)> _, CancellationToken token) =>
                Task.FromCanceled<List<OrphanedSid>>(token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.DiscoverOrphanedSidsAsync(
                new List<string>(),
                progress,
                ct));
    }

    [Fact]
    public void BuildMappings_SidsAlreadyMatch_SkipsMapping()
    {
        // When old SID == new SID, no migration is needed.
        var creds = new List<CredentialEntry>
        {
            new() { Sid = OldSid1 }
        };
        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [OldSid1] = "TestUser"
        };
        // Local account has the SAME SID as the stored credential.
        var localAccounts = new List<LocalUserAccount> { new("TestUser", OldSid1) };

        var result = _service.BuildMappings(creds, localAccounts, sidNames);

        Assert.Empty(result);
    }

    // --- DeleteSidsFromAppData tests ---

    [Fact]
    public void DeleteSidsFromAppData_RemovesMatchingCredsAppsCallers()
    {
        const string otherSid = "S-1-5-21-3333333333-3333333333-3333333333-1003";
        var database = new AppDatabase
        {
            Apps =
            [
                new() { Id = "a1", AccountSid = OldSid1 },
                new() { Id = "a2", AccountSid = otherSid }
            ]
        };
        database.GetOrCreateAccount(OldSid1).IsIpcCaller = true;
        database.GetOrCreateAccount(otherSid).IsIpcCaller = true;
        var store = new CredentialStore
        {
            Credentials =
            [
                new() { Sid = OldSid1 },
                new() { Sid = otherSid }
            ]
        };

        _testDatabase = database;


        var (creds, apps, callers) = _service.DeleteSidsFromAppData(new List<string> { OldSid1 }, store);

        Assert.Equal(1, creds);
        Assert.Equal(1, apps);
        Assert.Equal(1, callers);
        Assert.Single(database.Apps);
        Assert.Equal("a2", database.Apps[0].Id);
        Assert.Single(database.Accounts, a => a.IsIpcCaller);
        Assert.Single(store.Credentials);
    }

    [Fact]
    public void DeleteSidsFromAppData_CleansPerAppReferences()
    {
        var database = new AppDatabase
        {
            Apps =
            [
                new()
                {
                    Id = "a1",
                    AccountSid = "other-sid",
                    AllowedIpcCallers = [OldSid1],
                    AllowedAclEntries = [new() { Sid = OldSid1, AllowExecute = true }]
                }
            ]
        };
        var store = new CredentialStore();

        _testDatabase = database;


        var (creds, apps, callers) = _service.DeleteSidsFromAppData(new List<string> { OldSid1 }, store);

        Assert.Equal(0, creds);
        Assert.Equal(0, apps); // app has different AccountSid
        Assert.Equal(0, callers); // no AccountEntry with IsIpcCaller for OldSid1
        Assert.Single(database.Apps);
        Assert.Empty(database.Apps[0].AllowedIpcCallers!);
        Assert.Empty(database.Apps[0].AllowedAclEntries!);
    }

    [Fact]
    public void DeleteSidsFromAppData_CaseInsensitiveMatch()
    {
        var database = new AppDatabase
        {
            Apps = [new() { Id = "a1", AccountSid = OldSid1.ToUpperInvariant() }]
        };
        var store = new CredentialStore
        {
            Credentials = [new() { Sid = OldSid1.ToUpperInvariant() }]
        };

        _testDatabase = database;


        var (creds, apps, _) = _service.DeleteSidsFromAppData(
            new List<string> { OldSid1.ToLowerInvariant() }, store);

        Assert.Equal(1, creds);
        Assert.Equal(1, apps);
        Assert.Empty(database.Apps);
        Assert.Empty(store.Credentials);
    }

    [Fact]
    public void DeleteSidsFromAppData_EmptySidList_NoOp()
    {
        var database = new AppDatabase
        {
            Apps = [new() { Id = "a1", AccountSid = OldSid1 }]
        };
        var store = new CredentialStore
        {
            Credentials = [new() { Sid = OldSid1 }]
        };

        _testDatabase = database;


        var (creds, apps, callers) = _service.DeleteSidsFromAppData(new List<string>(), store);

        Assert.Equal(0, creds);
        Assert.Equal(0, apps);
        Assert.Equal(0, callers);
        Assert.Single(database.Apps);
        Assert.Single(store.Credentials);
    }

    [Fact]
    public void DeleteSidsFromAppData_NonMatchingSid_CountsZero()
    {
        var database = new AppDatabase
        {
            Apps = [new() { Id = "a1", AccountSid = OldSid1 }]
        };
        database.GetOrCreateAccount(OldSid1).IsIpcCaller = true;
        var store = new CredentialStore
        {
            Credentials = [new() { Sid = OldSid1 }]
        };

        _testDatabase = database;


        var (creds, apps, callers) = _service.DeleteSidsFromAppData(
            new List<string> { "S-1-5-21-0000000000-0000000000-0000000000-9999" }, store);

        Assert.Equal(0, creds);
        Assert.Equal(0, apps);
        Assert.Equal(0, callers);
        Assert.Single(database.Apps);
        Assert.Single(database.Accounts, a => a.IsIpcCaller);
        Assert.Single(store.Credentials);
    }

    [Fact]
    public void MigrateAppData_DuplicateCredentialSid_RemovesOrphanedOldSidCredential()
    {
        // If a credential with the target SID already exists, the old-SID credential is an orphan
        // (its account has been re-created with a new SID) and must be removed from the store so
        // it does not appear as an unresolvable SID in the RunAs dialog.
        var credentialStore = new CredentialStore
        {
            Credentials =
            [
                new() { Sid = OldSid1 },
                new() { Sid = NewSid1 }
            ]
        };

        var database = new AppDatabase();
        var mappings = new List<SidMigrationMapping> { new(OldSid1, NewSid1, "OldUser") };

        _testDatabase = database;

        var counts = _service.MigrateAppData(mappings, credentialStore);

        Assert.Equal(0, counts.Credentials); // Skipped (not migrated — orphan removed instead)
        Assert.Single(credentialStore.Credentials); // Old-SID orphan was removed
        Assert.Equal(NewSid1, credentialStore.Credentials[0].Sid); // Only the new-SID credential remains
    }

    [Fact]
    public void MigrateAppData_ReplaceSidInList_DeduplicatesBySid()
    {
        // Two global IPC callers (AccountEntries) with different old SIDs map to the same new SID
        var database = new AppDatabase();
        database.GetOrCreateAccount(OldSid1).IsIpcCaller = true;
        database.GetOrCreateAccount(OldSid2).IsIpcCaller = true;
        var credentialStore = new CredentialStore();

        // Both old SIDs map to the same new SID
        var mappings = new List<SidMigrationMapping>
        {
            new(OldSid1, NewSid1, "User1"),
            new(OldSid2, NewSid1, "User2")
        };

        _testDatabase = database;


        var counts = _service.MigrateAppData(mappings, credentialStore);

        Assert.Equal(2, counts.IpcCallers); // Both migrated
        Assert.True(database.GetAccount(NewSid1)?.IsIpcCaller);
        Assert.Equal(1, database.Accounts.Count(a =>
            string.Equals(a.Sid, NewSid1, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void MigrateAppData_CopiesSidNamesFromOldToNew()
    {
        // Arrange
        var database = new AppDatabase
        {
            SidNames =
            {
                [OldSid1] = "alice"
            }
        };

        var mappings = new List<SidMigrationMapping>
        {
            new(OldSid1, NewSid1, "alice")
        };

        // Act
        _testDatabase = database;

        _service.MigrateAppData(mappings, new CredentialStore());

        // Assert — name copy delegated to cache service
        _sidNameCache.Verify(c => c.UpdateName(NewSid1, "alice"), Times.Once);
    }

    // --- DeleteSidsFromAppData: tray flags and ephemeral accounts cleanup ---

    public static TheoryData<string, Action<AppDatabase, string>, Func<AppDatabase, string, bool>> TrayAccessors => new()
    {
        {
            "TrayDiscovery",
            (db, sid) => db.GetOrCreateAccount(sid).TrayDiscovery = true,
            (db, sid) => db.GetAccount(sid)?.TrayDiscovery == true
        },
        {
            "TrayFolderBrowser",
            (db, sid) => db.GetOrCreateAccount(sid).TrayFolderBrowser = true,
            (db, sid) => db.GetAccount(sid)?.TrayFolderBrowser == true
        },
        {
            "TrayTerminal",
            (db, sid) => db.GetOrCreateAccount(sid).TrayTerminal = true,
            (db, sid) => db.GetAccount(sid)?.TrayTerminal == true
        },
    };

    [Theory]
    [MemberData(nameof(TrayAccessors))]
    public void DeleteSidsFromAppData_ClearsTrayFlag(string _, Action<AppDatabase, string> setFlag, Func<AppDatabase, string, bool> hasFlag)
    {
        var database = new AppDatabase();
        setFlag(database, OldSid1);
        setFlag(database, OldSid2); // different SID, should stay
        var store = new CredentialStore();

        _testDatabase = database;


        _service.DeleteSidsFromAppData(new List<string> { OldSid1 }, store);

        Assert.False(hasFlag(database, OldSid1));
        Assert.True(hasFlag(database, OldSid2));
    }

    [Fact]
    public void DeleteSidsFromAppData_CleansEphemeralAccounts()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(OldSid1).DeleteAfterUtc = DateTime.UtcNow.AddHours(24);
        database.GetOrCreateAccount(OldSid2).DeleteAfterUtc = DateTime.UtcNow.AddHours(24);
        var store = new CredentialStore();

        _testDatabase = database;


        _service.DeleteSidsFromAppData(new List<string> { OldSid1 }, store);

        Assert.Null(database.GetAccount(OldSid1));
        Assert.NotNull(database.GetAccount(OldSid2));
        Assert.True(database.GetAccount(OldSid2)!.DeleteAfterUtc.HasValue);
    }

    [Fact]
    public void DeleteSidsFromAppData_CleansAllTrayAndEphemeralInOneCall()
    {
        // Multiple cleanup targets are all cleaned in a single call
        var database = new AppDatabase();
        var account = database.GetOrCreateAccount(OldSid1);
        account.TrayDiscovery = true;
        account.TrayFolderBrowser = true;
        account.TrayTerminal = true;
        account.DeleteAfterUtc = DateTime.UtcNow.AddHours(24);
        var store = new CredentialStore();
        store.Credentials.Add(new CredentialEntry { Sid = OldSid1 });

        _testDatabase = database;


        _service.DeleteSidsFromAppData(new List<string> { OldSid1 }, store);

        Assert.Null(database.GetAccount(OldSid1));
        Assert.Empty(store.Credentials);
    }

    [Fact]
    public void BuildMappings_SidStillResolves_SkipsMapping()
    {
        // S-1-5-18 is the LocalSystem SID and always resolves on any Windows machine.
        const string resolvableSid = "S-1-5-18";
        var creds = new List<CredentialEntry>
        {
            new() { Sid = resolvableSid }
        };
        var localAccounts = new List<LocalUserAccount> { new("SYSTEM", NewSid1) };

        // Because TryResolveName("S-1-5-18") returns a non-null name, the credential
        // is skipped and no mapping is generated even though a local account name matches.
        var result = _service.BuildMappings(creds, localAccounts);

        Assert.Empty(result);
    }

    // --- T6: MigrateAppData tray flags ---

    [Theory]
    [MemberData(nameof(TrayAccessors))]
    public void MigrateAppData_UpdatesTrayFlag(string _, Action<AppDatabase, string> setFlag, Func<AppDatabase, string, bool> hasFlag)
    {
        var database = new AppDatabase();
        setFlag(database, OldSid1);
        setFlag(database, OldSid2);

        var mappings = new List<SidMigrationMapping> { new(OldSid1, NewSid1, "User1") };

        _testDatabase = database;


        _service.MigrateAppData(mappings, new CredentialStore());

        Assert.True(hasFlag(database, NewSid1));
        Assert.False(hasFlag(database, OldSid1));
        Assert.True(hasFlag(database, OldSid2)); // unmapped — unchanged
    }

    [Theory]
    [MemberData(nameof(TrayAccessors))]
    public void MigrateAppData_TrayFlag_DeduplicatesWhenTwoMapToSame(string _, Action<AppDatabase, string> setFlag, Func<AppDatabase, string, bool> hasFlag)
    {
        var database = new AppDatabase();
        setFlag(database, OldSid1);
        setFlag(database, OldSid2);

        var mappings = new List<SidMigrationMapping>
        {
            new(OldSid1, NewSid1, "User1"),
            new(OldSid2, NewSid1, "User2")
        };

        _testDatabase = database;


        _service.MigrateAppData(mappings, new CredentialStore());

        Assert.True(hasFlag(database, NewSid1));
        Assert.Equal(1, database.Accounts.Count(a =>
            string.Equals(a.Sid, NewSid1, StringComparison.OrdinalIgnoreCase)));
    }

    // --- EphemeralAccounts migration ---

    [Fact]
    public void MigrateAppData_UpdatesEphemeralAccountSids()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(OldSid1).DeleteAfterUtc = DateTime.UtcNow.AddHours(24);
        database.GetOrCreateAccount(OldSid2).DeleteAfterUtc = DateTime.UtcNow.AddHours(24);

        var mappings = new List<SidMigrationMapping>
        {
            new(OldSid1, NewSid1, "User1")
        };

        _testDatabase = database;


        _service.MigrateAppData(mappings, new CredentialStore());

        Assert.Null(database.GetAccount(OldSid1)); // entry renamed to NewSid1
        Assert.True(database.GetAccount(NewSid1)?.DeleteAfterUtc.HasValue);
        Assert.True(database.GetAccount(OldSid2)?.DeleteAfterUtc.HasValue); // unmapped — unchanged
    }

    [Fact]
    public void MigrateAppData_EphemeralAccounts_DeduplicatesWhenTwoMapToSame()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(OldSid1).DeleteAfterUtc = DateTime.UtcNow.AddHours(24);
        database.GetOrCreateAccount(OldSid2).DeleteAfterUtc = DateTime.UtcNow.AddHours(12);

        var mappings = new List<SidMigrationMapping>
        {
            new(OldSid1, NewSid1, "User1"),
            new(OldSid2, NewSid1, "User2")
        };

        _testDatabase = database;


        _service.MigrateAppData(mappings, new CredentialStore());

        Assert.True(database.GetAccount(NewSid1)?.DeleteAfterUtc.HasValue);
        Assert.Equal(1, database.Accounts.Count(a =>
            string.Equals(a.Sid, NewSid1, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void MigrateAppData_EphemeralAccounts_UnmappedSidUnchanged()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(OldSid1).DeleteAfterUtc = DateTime.UtcNow.AddHours(1);

        _testDatabase = database;


        _service.MigrateAppData(new List<SidMigrationMapping>(), new CredentialStore());

        Assert.True(database.GetAccount(OldSid1)?.DeleteAfterUtc.HasValue);
    }

    // --- PrivilegeLevel migration/deletion ---

    [Theory]
    [InlineData(PrivilegeLevel.Basic)]
    [InlineData(PrivilegeLevel.HighestAllowed)]
    [InlineData(PrivilegeLevel.LowIntegrity)]
    public void MigrateAppData_UpdatesPrivilegeLevel(PrivilegeLevel privilegeLevel)
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(OldSid1).PrivilegeLevel = privilegeLevel;
        database.GetOrCreateAccount(OldSid2).PrivilegeLevel = privilegeLevel;

        var mappings = new List<SidMigrationMapping>
        {
            new(OldSid1, NewSid1, "User1")
        };

        _testDatabase = database;
        _service.MigrateAppData(mappings, new CredentialStore());

        Assert.Equal(privilegeLevel, database.GetAccount(NewSid1)?.PrivilegeLevel);
        Assert.Null(database.GetAccount(OldSid1)); // renamed
        Assert.Equal(privilegeLevel, database.GetAccount(OldSid2)?.PrivilegeLevel); // OldSid2 unchanged
    }

    [Fact]
    public void MigrateAppData_PrivilegeLevel_DeduplicatesTakesHigherValue()
    {
        // When two SIDs map to the same new SID, the higher PrivilegeLevel wins.
        var database = new AppDatabase();
        database.GetOrCreateAccount(OldSid1).PrivilegeLevel = PrivilegeLevel.Basic;
        database.GetOrCreateAccount(OldSid2).PrivilegeLevel = PrivilegeLevel.LowIntegrity;

        var mappings = new List<SidMigrationMapping>
        {
            new(OldSid1, NewSid1, "User1"),
            new(OldSid2, NewSid1, "User2")
        };

        _testDatabase = database;
        _service.MigrateAppData(mappings, new CredentialStore());

        Assert.Equal(PrivilegeLevel.LowIntegrity, database.GetAccount(NewSid1)?.PrivilegeLevel);
        Assert.Equal(1, database.Accounts.Count(a =>
            string.Equals(a.Sid, NewSid1, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void DeleteSidsFromAppData_CleansPrivilegeLevel()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(OldSid1).PrivilegeLevel = PrivilegeLevel.LowIntegrity;
        database.GetOrCreateAccount(OldSid2).PrivilegeLevel = PrivilegeLevel.LowIntegrity;
        var store = new CredentialStore();

        _testDatabase = database;
        _service.DeleteSidsFromAppData(new List<string> { OldSid1 }, store);

        Assert.Null(database.GetAccount(OldSid1));
        Assert.Equal(PrivilegeLevel.LowIntegrity, database.GetAccount(OldSid2)?.PrivilegeLevel);
    }

    // --- AccountGrants migration ---

    [Fact]
    public void MigrateAppData_MigratesAccountGrantsKeys()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(OldSid1).Grants.Add(new GrantedPathEntry { Path = @"C:\foo" });

        var mappings = new List<SidMigrationMapping> { new(OldSid1, NewSid1, "TestUser") };
        _testDatabase = database;

        _service.MigrateAppData(mappings, new CredentialStore());

        Assert.Null(database.GetAccount(OldSid1));
        var newGrants = database.GetAccount(NewSid1)?.Grants;
        Assert.NotNull(newGrants);
        Assert.Single(newGrants);
        Assert.Equal(@"C:\foo", newGrants[0].Path);
    }

    [Fact]
    public void MigrateAppData_AccountGrantsMerge_Deduplicates()
    {
        // Both old and new SID already have grants on the same path — no duplicate after merge
        var database = new AppDatabase();
        database.GetOrCreateAccount(OldSid1).Grants.Add(new GrantedPathEntry { Path = @"C:\shared", IsDeny = false });
        database.GetAccount(OldSid1)!.Grants.Add(new GrantedPathEntry { Path = @"C:\extra" });
        database.GetOrCreateAccount(NewSid1).Grants.Add(new GrantedPathEntry { Path = @"C:\shared", IsDeny = false }); // duplicate

        var mappings = new List<SidMigrationMapping> { new(OldSid1, NewSid1, "TestUser") };
        _testDatabase = database;

        _service.MigrateAppData(mappings, new CredentialStore());

        Assert.Null(database.GetAccount(OldSid1));
        var grants = database.GetAccount(NewSid1)?.Grants;
        Assert.NotNull(grants);
        // The shared entry should appear once; the extra entry should also be present
        Assert.Equal(2, grants.Count);
        Assert.Contains(grants, e => e.Path == @"C:\shared");
        Assert.Contains(grants, e => e.Path == @"C:\extra");
    }

    [Fact]
    public void MigrateAppData_AccountGrantsNull_NoError()
    {
        var database = new AppDatabase(); // No AccountEntries
        var mappings = new List<SidMigrationMapping> { new(OldSid1, NewSid1, "TestUser") };

        _testDatabase = database;
        var exception = Record.Exception(() =>
            _service.MigrateAppData(mappings, new CredentialStore()));

        Assert.Null(exception);
        Assert.Null(database.GetAccount(OldSid1));
        Assert.Null(database.GetAccount(NewSid1));
    }

    // --- FirewallSettings migration/deletion ---

    [Fact]
    public void MigrateAppData_MigratesFirewallSettingsKeys()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(OldSid1).Firewall = new FirewallAccountSettings { AllowInternet = false };

        var mappings = new List<SidMigrationMapping> { new(OldSid1, NewSid1, "TestUser") };
        _testDatabase = database;

        _service.MigrateAppData(mappings, new CredentialStore());

        Assert.Null(database.GetAccount(OldSid1));
        Assert.False(database.GetAccount(NewSid1)?.Firewall.AllowInternet);
    }

    [Fact]
    public void MigrateAppData_FirewallSettings_NewSidWinsOnConflict()
    {
        // Both old and new SID have firewall settings — new SID's settings are kept
        var database = new AppDatabase();
        database.GetOrCreateAccount(OldSid1).Firewall = new FirewallAccountSettings { AllowInternet = false };
        database.GetOrCreateAccount(NewSid1).Firewall = new FirewallAccountSettings { AllowInternet = true, AllowLan = false };

        var mappings = new List<SidMigrationMapping> { new(OldSid1, NewSid1, "TestUser") };
        _testDatabase = database;

        _service.MigrateAppData(mappings, new CredentialStore());

        Assert.Null(database.GetAccount(OldSid1));
        // NewSid1 settings preserved (new SID wins)
        Assert.True(database.GetAccount(NewSid1)?.Firewall.AllowInternet);
        Assert.False(database.GetAccount(NewSid1)?.Firewall.AllowLan);
    }

    [Fact]
    public void MigrateAppData_FirewallSettingsNull_NoError()
    {
        var database = new AppDatabase(); // No AccountEntries with firewall settings
        var mappings = new List<SidMigrationMapping> { new(OldSid1, NewSid1, "TestUser") };

        _testDatabase = database;
        var exception = Record.Exception(() =>
            _service.MigrateAppData(mappings, new CredentialStore()));

        Assert.Null(exception);
    }

    [Fact]
    public void DeleteSidsFromAppData_CleansFirewallSettings()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(OldSid1).Firewall = new FirewallAccountSettings { AllowInternet = false };
        database.GetOrCreateAccount(OldSid2).Firewall = new FirewallAccountSettings { AllowLocalhost = false };

        _testDatabase = database;


        _service.DeleteSidsFromAppData(new List<string> { OldSid1 }, new CredentialStore());

        Assert.Null(database.GetAccount(OldSid1));
        Assert.NotNull(database.GetAccount(OldSid2));
        Assert.False(database.GetAccount(OldSid2)!.Firewall.IsDefault);
    }

    // --- AccountGroupSnapshots migration ---

    [Fact]
    public void MigrateAppData_MigratesAccountGroupSnapshotKeys()
    {
        // AccountGroupSnapshots maps SID → list of group SIDs. Migration renames the key.
        var database = new AppDatabase
        {
            AccountGroupSnapshots = new(StringComparer.OrdinalIgnoreCase)
            {
                [OldSid1] = ["S-1-5-32-544", "S-1-5-32-545"],
                [OldSid2] = ["S-1-5-32-545"] // unmapped — stays
            }
        };

        var mappings = new List<SidMigrationMapping> { new(OldSid1, NewSid1, "TestUser") };
        _testDatabase = database;

        _service.MigrateAppData(mappings, new CredentialStore());

        // Old key removed, new key present with same group SIDs
        Assert.False(database.AccountGroupSnapshots!.ContainsKey(OldSid1));
        Assert.True(database.AccountGroupSnapshots.ContainsKey(NewSid1));
        Assert.Contains("S-1-5-32-544", database.AccountGroupSnapshots[NewSid1]);
        // Unmapped SID unchanged
        Assert.True(database.AccountGroupSnapshots.ContainsKey(OldSid2));
    }

    [Fact]
    public void MigrateAppData_AccountGroupSnapshots_MergesWhenNewSidAlreadyExists()
    {
        // When both old and new SIDs have snapshots, groups are merged without duplicates.
        var database = new AppDatabase
        {
            AccountGroupSnapshots = new(StringComparer.OrdinalIgnoreCase)
            {
                [OldSid1] = ["S-1-5-32-544", "S-1-5-32-546"],
                [NewSid1] = ["S-1-5-32-544", "S-1-5-32-547"] // overlap on S-1-5-32-544
            }
        };

        var mappings = new List<SidMigrationMapping> { new(OldSid1, NewSid1, "TestUser") };
        _testDatabase = database;

        _service.MigrateAppData(mappings, new CredentialStore());

        Assert.False(database.AccountGroupSnapshots.ContainsKey(OldSid1));
        var merged = database.AccountGroupSnapshots[NewSid1];
        Assert.Contains("S-1-5-32-544", merged);
        Assert.Contains("S-1-5-32-546", merged);
        Assert.Contains("S-1-5-32-547", merged);
        Assert.Equal(3, merged.Distinct(StringComparer.OrdinalIgnoreCase).Count()); // no duplicates
    }

    // --- BuildMappings: InteractiveUser exclusion ---

    [Fact]
    public void BuildMappings_InteractiveUserCredential_Excluded()
    {
        // A CredentialEntry whose SID matches the current interactive user SID must be skipped.
        // IsInteractiveUser uses SidResolutionHelper.GetInteractiveUserSid() — if null (no explorer),
        // this credential is treated as a regular account and may be included instead.
        // The test only verifies exclusion when the interactive SID is resolvable.
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSid == null)
            return; // no active desktop session — test is not applicable

        var creds = new List<CredentialEntry>
        {
            new() { Sid = interactiveSid }
        };
        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [interactiveSid] = "InteractiveUser"
        };
        var localAccounts = new List<LocalUserAccount> { new("InteractiveUser", NewSid1) };

        var result = _service.BuildMappings(creds, localAccounts, sidNames);

        // Interactive user credential is excluded from migration
        Assert.Empty(result);
    }

    // --- BuildMappings: empty SID skip ---

    [Fact]
    public void BuildMappings_EmptySidCredential_Skipped()
    {
        // CredentialEntry with empty Sid must be skipped to avoid invalid SID lookups.
        var creds = new List<CredentialEntry>
        {
            new() { Sid = string.Empty }
        };
        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var localAccounts = new List<LocalUserAccount> { new("SomeUser", NewSid1) };

        var result = _service.BuildMappings(creds, localAccounts, sidNames);

        Assert.Empty(result);
    }

    // --- BuildMappings: machine prefix stripping ---

    [Fact]
    public void BuildMappings_MachinePrefixInSidName_StrippedForLocalAccountMatch()
    {
        // SidNames entry uses "MACHINE\username" format — the prefix must be stripped before
        // matching against local account names.
        var creds = new List<CredentialEntry>
        {
            new() { Sid = OldSid1 }
        };
        var sidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [OldSid1] = @"DESKTOP-XYZ\alice" // machine-prefixed name
        };
        var localAccounts = new List<LocalUserAccount> { new("alice", NewSid1) };

        var result = _service.BuildMappings(creds, localAccounts, sidNames);

        // "alice" extracted after stripping "DESKTOP-XYZ\" and matched to local account
        Assert.Single(result);
        Assert.Equal(OldSid1, result[0].OldSid);
        Assert.Equal(NewSid1, result[0].NewSid);
    }
}

/// <summary>
/// Tests for SidCleanupHelper — both account-SID cleanup and AppContainer cleanup paths.
/// </summary>
public class SidCleanupHelperTests
{
    private const string TestSid = "S-1-5-21-9999999999-9999999999-9999999999-1001";
    private const string ContainerSid = "S-1-15-2-1234-5678";

    // --- CleanupContainerFromAppData ---

    [Fact]
    public void CleanupContainerFromAppData_RemovesReferencingAppEntries()
    {
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Name = "SandboxedApp", AppContainerName = "ram_target" });
        database.Apps.Add(new AppEntry { Name = "OtherContainerApp", AppContainerName = "ram_other" });
        database.Apps.Add(new AppEntry { Name = "AccountApp", AccountSid = TestSid });
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_target" });

        var (removedApps, removedContainers) =
            new SidCleanupHelper(new LambdaDatabaseProvider(() => database)).CleanupContainerFromAppData("ram_target");

        Assert.Equal(1, removedApps);
        Assert.Equal(1, removedContainers);
        Assert.Equal(2, database.Apps.Count); // OtherContainerApp + AccountApp remain
        Assert.Contains(database.Apps, a => a.Name == "OtherContainerApp");
        Assert.Contains(database.Apps, a => a.Name == "AccountApp");
        Assert.Empty(database.AppContainers);
    }

    [Fact]
    public void CleanupContainerFromAppData_RemovesContainerEntryFromList()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_target", DisplayName = "Target" });
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_other", DisplayName = "Other" });

        new SidCleanupHelper(new LambdaDatabaseProvider(() => database)).CleanupContainerFromAppData("ram_target");

        Assert.Single(database.AppContainers);
        Assert.Equal("ram_other", database.AppContainers[0].Name);
    }

    [Fact]
    public void CleanupContainerFromAppData_WithContainerSid_CleansPerAppReferences()
    {
        var database = new AppDatabase();
        var otherApp = new AppEntry
        {
            Name = "OtherApp",
            AccountSid = TestSid,
            AllowedIpcCallers = [ContainerSid],
            AllowedAclEntries = [new AllowAclEntry { Sid = ContainerSid }]
        };
        database.Apps.Add(new AppEntry { Name = "ContainerApp", AppContainerName = "ram_target" });
        database.Apps.Add(otherApp);
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_target" });

        new SidCleanupHelper(new LambdaDatabaseProvider(() => database)).CleanupContainerFromAppData("ram_target", ContainerSid);

        // ContainerApp removed; OtherApp stays but its per-app references to the container SID are cleaned
        Assert.Single(database.Apps);
        Assert.Empty(database.Apps[0].AllowedIpcCallers!);
        Assert.Empty(database.Apps[0].AllowedAclEntries!);
    }

    [Fact]
    public void CleanupContainerFromAppData_WithoutContainerSid_DoesNotCleanPerAppReferences()
    {
        var database = new AppDatabase();
        var otherApp = new AppEntry
        {
            Name = "OtherApp",
            AccountSid = TestSid,
            AllowedAclEntries = [new AllowAclEntry { Sid = ContainerSid }]
        };
        database.Apps.Add(otherApp);
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_target" });

        // No containerSid passed → per-app references not touched
        new SidCleanupHelper(new LambdaDatabaseProvider(() => database)).CleanupContainerFromAppData("ram_target", containerSid: null);

        Assert.Single(database.Apps[0].AllowedAclEntries!);
    }

    [Fact]
    public void CleanupContainerFromAppData_CaseInsensitive()
    {
        var database = new AppDatabase();
        database.Apps.Add(new AppEntry { Name = "App", AppContainerName = "RAM_TARGET" });
        database.AppContainers.Add(new AppContainerEntry { Name = "RAM_TARGET" });

        var (removedApps, _) = new SidCleanupHelper(new LambdaDatabaseProvider(() => database)).CleanupContainerFromAppData("ram_target");

        Assert.Equal(1, removedApps);
        Assert.Empty(database.Apps);
    }

    // --- AccountGrants cleanup (via AccountEntry removal) ---

    [Fact]
    public void CleanupSidFromAppData_RemovesAccountEntry()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(TestSid).Grants.Add(new GrantedPathEntry { Path = @"C:\foo" });
        database.GetOrCreateAccount("S-1-5-21-other").Grants.Add(new GrantedPathEntry { Path = @"C:\bar" });

        new SidCleanupHelper(new LambdaDatabaseProvider(() => database)).CleanupSidFromAppData(TestSid);

        Assert.Null(database.GetAccount(TestSid));
        Assert.NotNull(database.GetAccount("S-1-5-21-other"));
    }

    [Fact]
    public void CleanupContainerFromAppData_RemovesAccountEntryForContainerSid()
    {
        var database = new AppDatabase();
        database.GetOrCreateAccount(ContainerSid).Grants.Add(new GrantedPathEntry { Path = @"C:\baz", IsTraverseOnly = true });
        database.GetOrCreateAccount(TestSid).Grants.Add(new GrantedPathEntry { Path = @"C:\qux" });
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_target" });

        new SidCleanupHelper(new LambdaDatabaseProvider(() => database)).CleanupContainerFromAppData("ram_target", ContainerSid);

        Assert.Null(database.GetAccount(ContainerSid));
        Assert.NotNull(database.GetAccount(TestSid));
    }

    // --- FirewallSettings cleanup (via AccountEntry removal) ---

    [Fact]
    public void CleanupSidFromAppData_RemovesFirewallSettings()
    {
        const string otherSid = "S-1-5-21-other";
        var database = new AppDatabase();
        database.GetOrCreateAccount(TestSid).Firewall = new FirewallAccountSettings { AllowInternet = false };
        database.GetOrCreateAccount(otherSid).Firewall = new FirewallAccountSettings { AllowLocalhost = false };

        new SidCleanupHelper(new LambdaDatabaseProvider(() => database)).CleanupSidFromAppData(TestSid);

        Assert.Null(database.GetAccount(TestSid));
        Assert.NotNull(database.GetAccount(otherSid));
        Assert.False(database.GetAccount(otherSid)!.Firewall.IsDefault);
    }

    [Fact]
    public void CleanupSidFromAppData_NoAccountEntry_NoError()
    {
        var database = new AppDatabase(); // No AccountEntry for TestSid

        var exception = Record.Exception(() =>
            new SidCleanupHelper(new LambdaDatabaseProvider(() => database)).CleanupSidFromAppData(TestSid));

        Assert.Null(exception);
    }
}