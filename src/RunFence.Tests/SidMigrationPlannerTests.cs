using RunFence.Core.Models;
using RunFence.SidMigration;
using Xunit;

namespace RunFence.Tests;

public sealed class SidMigrationPlannerTests
{
    [Fact]
    public void DataMappingPlanner_BuildPlan_ComputesCountsWithoutMutatingInputs()
    {
        var oldSidOne = "S-1-old-1";
        var oldSidTwo = "S-1-old-2";
        var newSid = "S-1-new";
        var planner = new SidMigrationDataMappingPlanner(new SidMigrationCoreMutationService());
        var database = new AppDatabase
        {
            Apps =
            [
                new AppEntry
                {
                    Id = "app-1",
                    AccountSid = oldSidOne,
                    AllowedIpcCallers = [oldSidOne],
                    AllowedAclEntries =
                    [
                        new AllowAclEntry { Sid = oldSidOne, AllowExecute = true },
                        new AllowAclEntry { Sid = oldSidTwo, AllowWrite = true }
                    ]
                }
            ],
            Accounts =
            [
                new AccountEntry { Sid = oldSidOne, IsIpcCaller = true },
                new AccountEntry { Sid = oldSidTwo, IsIpcCaller = true }
            ]
        };
        var credentialStore = new CredentialStore
        {
            Credentials =
            [
                new CredentialEntry { Sid = oldSidOne, EncryptedPassword = [1] },
                new CredentialEntry { Sid = newSid, EncryptedPassword = [2] }
            ]
        };
        IReadOnlyList<SidMigrationMapping> mappings =
        [
            new SidMigrationMapping(oldSidOne, newSid, "user-1"),
            new SidMigrationMapping(oldSidTwo, newSid, "user-2")
        ];

        var plan = planner.BuildPlan(mappings, credentialStore, database);

        Assert.Equal(mappings, plan.Mappings);
        Assert.Equal(0, plan.Counts.Credentials);
        Assert.Equal(1, plan.Counts.Apps);
        Assert.Equal(3, plan.Counts.IpcCallers);
        Assert.Equal(2, plan.Counts.AllowEntries);

        Assert.Equal(oldSidOne, database.Apps[0].AccountSid);
        Assert.Equal([oldSidOne], database.Apps[0].AllowedIpcCallers);
        var allowedAclEntries = Assert.IsAssignableFrom<IReadOnlyList<AllowAclEntry>>(database.Apps[0].AllowedAclEntries);
        Assert.Equal(oldSidOne, allowedAclEntries[0].Sid);
        Assert.Equal(oldSidTwo, allowedAclEntries[1].Sid);
        Assert.Equal(
            [oldSidOne, newSid],
            credentialStore.Credentials.Select(credential => credential.Sid).ToArray());
    }

    [Fact]
    public void DataMappingPlanner_FormatMigrationMessage_UsesCounts()
    {
        var planner = new SidMigrationDataMappingPlanner(new SidMigrationCoreMutationService());

        var message = planner.FormatMigrationMessage(new MigrationCounts(1, 2, 3, 4));

        Assert.Equal("Migrated 1 credential(s), 2 app(s), 3 IPC caller(s), 4 allow entry/entries.", message);
    }

    [Fact]
    public void DataMappingPlanner_BuildPlan_DuplicateOldSid_LastMappingWins()
    {
        var coreMutationService = new SidMigrationCoreMutationService();
        var planner = new SidMigrationDataMappingPlanner(coreMutationService);
        var database = new AppDatabase
        {
            Apps = [new AppEntry { Id = "app-1", AccountSid = "S-1-old" }]
        };
        var store = new CredentialStore
        {
            Credentials = [new CredentialEntry { Sid = "S-1-old", EncryptedPassword = [1] }]
        };
        var firstTargetSid = "S-1-new-first";
        var lastTargetSid = "S-1-new-last";

        var plan = planner.BuildPlan(
            [
                new SidMigrationMapping("S-1-old", firstTargetSid, "user-1"),
                new SidMigrationMapping("S-1-old", lastTargetSid, "user-2")
            ],
            store,
            database);

        var plannedDatabase = database.CreateSnapshot();
        var plannedStore = store.CreateSnapshot();
        coreMutationService.ApplyCoreMappings(plan.Mappings, plannedStore, plannedDatabase);

        Assert.Equal(1, plan.Counts.Credentials);
        Assert.Equal(1, plan.Counts.Apps);
        Assert.Equal(lastTargetSid, Assert.Single(plannedStore.Credentials).Sid);
        Assert.Equal(lastTargetSid, Assert.Single(plannedDatabase.Apps).AccountSid);
    }

    [Fact]
    public void DeletionPlanner_BuildPlan_SelectsAppsForDeletedSidsCaseInsensitively()
    {
        var deletedSid = "S-1-delete";
        var planner = new SidMigrationDeletionPlanner();
        var snapshot = new AppDatabase
        {
            Apps =
            [
                new AppEntry { Id = "app-1", AccountSid = deletedSid.ToUpperInvariant(), ManageShortcuts = true },
                new AppEntry { Id = "app-2", AccountSid = deletedSid, ManageShortcuts = false },
                new AppEntry { Id = "app-3", AccountSid = "S-1-keep", ManageShortcuts = true }
            ]
        };

        var plan = planner.BuildPlan([deletedSid.ToLowerInvariant()], snapshot);

        Assert.Equal([deletedSid.ToLowerInvariant()], plan.SidsToDelete);
        Assert.Single(plan.AppsNeedingShortcutCleanup);
        Assert.Equal("app-1", plan.AppsNeedingShortcutCleanup[0].Id);
    }
}
