using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AppConfigIndexTests
{
    private static AppConfigIndex CreateIndex(AppDatabase database)
    {
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        return new AppConfigIndex(ownershipProjection, new AppIdValidator());
    }

    [Fact]
    public void FilterForMainConfig_Settings_MutationDoesNotAffectOriginal()
    {
        var db = new AppDatabase();
        db.Settings.IdleTimeoutMinutes = 15;
        db.Settings.AutoStartOnLogin = true;
        db.Settings.LogVerbosity = LogVerbosity.Error;
        db.Settings.NagEligible = true;

        var filtered = CreateIndex(db).FilterForMainConfig(db);

        filtered.Settings.IdleTimeoutMinutes = 99;
        filtered.Settings.AutoStartOnLogin = false;
        filtered.Settings.LogVerbosity = LogVerbosity.Debug;
        filtered.Settings.NagEligible = false;

        Assert.Equal(15, db.Settings.IdleTimeoutMinutes);
        Assert.True(db.Settings.AutoStartOnLogin);
        Assert.Equal(LogVerbosity.Error, db.Settings.LogVerbosity);
        Assert.True(db.Settings.NagEligible);
    }

    [Fact]
    public void FilterForMainConfig_Settings_CollectionMutationDoesNotAffectOriginal()
    {
        var db = new AppDatabase();
        db.Settings.SeenDiskRootAclKeys.Add("root-key-1");
        db.Settings.SeenDiskRootAclKeys.Add("root-key-2");

        var filtered = CreateIndex(db).FilterForMainConfig(db);

        filtered.Settings.SeenDiskRootAclKeys.Clear();
        filtered.Settings.SeenDiskRootAclKeys.Add("mutated-key");

        Assert.Equal(2, db.Settings.SeenDiskRootAclKeys.Count);
        Assert.Contains("root-key-1", db.Settings.SeenDiskRootAclKeys);
        Assert.Contains("root-key-2", db.Settings.SeenDiskRootAclKeys);
    }

    [Fact]
    public void FilterForMainConfig_Settings_Clone_DoesNotShareHandlerMappingPathPrefixes()
    {
        var db = new AppDatabase();
        db.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new("app-1", null, ["C:\\Allowed"], true)
        };

        var filtered = CreateIndex(db).FilterForMainConfig(db);

        var originalEntry = db.Settings.HandlerMappings[".txt"];
        var filteredEntry = filtered.Settings.HandlerMappings![".txt"];
        Assert.NotNull(originalEntry.PathPrefixes);
        Assert.NotNull(filteredEntry.PathPrefixes);
        Assert.NotSame(originalEntry.PathPrefixes, filteredEntry.PathPrefixes);

        filteredEntry.PathPrefixes.Add("C:\\Another");
        filtered.Settings.HandlerMappings![".txt"] = filteredEntry;

        Assert.Single(originalEntry.PathPrefixes);
        Assert.Equal("C:\\Allowed", originalEntry.PathPrefixes[0]);
        Assert.Equal(2, filteredEntry.PathPrefixes.Count);
    }

    [Fact]
    public void FilterForMainConfig_ExtraConfigApps_Excluded()
    {
        var db = new AppDatabase();
        var mainApp = new AppEntry { Id = "main01", Name = "MainApp" };
        var extraApp = new AppEntry { Id = "extra01", Name = "ExtraApp" };
        db.Apps.Add(mainApp);
        db.Apps.Add(extraApp);

        var index = CreateIndex(db);
        index.AssignApp("extra01", "extra.rfn");

        var filtered = index.FilterForMainConfig(db);

        Assert.Single(filtered.Apps);
        Assert.Contains(filtered.Apps, a => a.Id == "main01");
        Assert.DoesNotContain(filtered.Apps, a => a.Id == "extra01");
    }

    [Fact]
    public void FilterForMainConfig_IncludesJobKeeperInstances_ForMainConfigPersistence()
    {
        var db = new AppDatabase
        {
            JobKeeperInstances = new Dictionary<string, JobKeeperInstanceIdentity>(StringComparer.OrdinalIgnoreCase)
            {
                ["sid|restricted"] = new()
                {
                    TargetSid = "S-1-5-21-1",
                    ExpectedMode = JobKeeperIntegrityMode.Restricted,
                    InstanceId = "instance",
                    PipeName = "pipe"
                }
            }
        };

        var filtered = CreateIndex(db).FilterForMainConfig(db);

        Assert.NotNull(filtered.JobKeeperInstances);
        Assert.True(filtered.JobKeeperInstances!.ContainsKey("sid|restricted"));
        Assert.NotSame(db.JobKeeperInstances, filtered.JobKeeperInstances);
    }

    [Fact]
    public void FilterForMainConfig_IncludesTrackingJobSids_AsIndependentClone()
    {
        var db = new AppDatabase
        {
            TrackingJobSids =
            [
                "S-1-5-21-1-2-3-1001",
                "s-1-5-21-1-2-3-1001",
                "S-1-5-21-1-2-3-1002"
            ]
        };

        var filtered = CreateIndex(db).FilterForMainConfig(db);

        Assert.Equal(
            ["S-1-5-21-1-2-3-1001", "S-1-5-21-1-2-3-1002"],
            filtered.TrackingJobSids);
        Assert.NotSame(db.TrackingJobSids, filtered.TrackingJobSids);
    }
}
