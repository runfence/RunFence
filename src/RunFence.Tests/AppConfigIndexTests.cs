using Moq;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AppConfigIndexTests
{
    private readonly Mock<IGrantConfigTracker> _grantConfigTracker = new();

    private AppConfigIndex CreateIndex() => new(_grantConfigTracker.Object);

    // --- F-08-test: FilterForMainConfig Settings isolation ---

    [Fact]
    public void FilterForMainConfig_Settings_MutationDoesNotAffectOriginal()
    {
        // Arrange: database with settings containing a non-default value
        var db = new AppDatabase();
        db.Settings.IdleTimeoutMinutes = 15;
        db.Settings.AutoStartOnLogin = true;
        db.Settings.LogVerbosity = LogVerbosity.Error;

        var index = CreateIndex();

        // Act: filter for main config → get a cloned Settings
        var filtered = index.FilterForMainConfig(db);

        // Mutate the filtered Settings
        filtered.Settings.IdleTimeoutMinutes = 99;
        filtered.Settings.AutoStartOnLogin = false;
        filtered.Settings.LogVerbosity = LogVerbosity.Debug;

        // Assert: original database Settings is unchanged
        Assert.Equal(15, db.Settings.IdleTimeoutMinutes);
        Assert.True(db.Settings.AutoStartOnLogin);
        Assert.Equal(LogVerbosity.Error, db.Settings.LogVerbosity);
    }

    [Fact]
    public void FilterForMainConfig_Settings_CollectionMutationDoesNotAffectOriginal()
    {
        // Arrange: Settings with a non-empty SeenDiskRootAclKeys list
        var db = new AppDatabase();
        db.Settings.SeenDiskRootAclKeys.Add("root-key-1");
        db.Settings.SeenDiskRootAclKeys.Add("root-key-2");

        var index = CreateIndex();

        // Act: filter → get cloned Settings
        var filtered = index.FilterForMainConfig(db);

        // Mutate the filtered Settings collection
        filtered.Settings.SeenDiskRootAclKeys.Clear();
        filtered.Settings.SeenDiskRootAclKeys.Add("mutated-key");

        // Assert: original Settings collection is unchanged
        Assert.Equal(2, db.Settings.SeenDiskRootAclKeys.Count);
        Assert.Contains("root-key-1", db.Settings.SeenDiskRootAclKeys);
        Assert.Contains("root-key-2", db.Settings.SeenDiskRootAclKeys);
    }

    // --- FilterForMainConfig: extra-config apps excluded ---

    [Fact]
    public void FilterForMainConfig_ExtraConfigApps_Excluded()
    {
        // Arrange: one main-config app and one extra-config app
        var db = new AppDatabase();
        var mainApp = new AppEntry { Id = "main01", Name = "MainApp" };
        var extraApp = new AppEntry { Id = "extra01", Name = "ExtraApp" };
        db.Apps.Add(mainApp);
        db.Apps.Add(extraApp);

        var index = CreateIndex();
        index.AssignApp("extra01", "extra.rfn");

        // Act
        var filtered = index.FilterForMainConfig(db);

        // Assert: only main-config app returned
        Assert.Single(filtered.Apps);
        Assert.Contains(filtered.Apps, a => a.Id == "main01");
        Assert.DoesNotContain(filtered.Apps, a => a.Id == "extra01");
    }
}
