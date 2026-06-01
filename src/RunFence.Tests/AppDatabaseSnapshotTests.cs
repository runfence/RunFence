using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AppDatabaseSnapshotTests
{
    [Fact]
    public void CreateSnapshot_DeepClonesMutableCollections()
    {
        var database = new AppDatabase();
        database.TrackingJobSids = ["S-1-5-21-1-2-3-1001"];
        database.Apps.Add(new AppEntry
        {
            Id = "app1",
            Name = "App",
            PathPrefixes = ["C:\\Allowed"],
            ShortcutProtectionStates =
            [
                new ShortcutProtectionState(@"C:\Links\App.lnk", true, false, true)
            ],
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["K"] = "V"
            },
            EnforcementRetryStatus = new AppEnforcementRetryStatus("failed", DateTime.UtcNow)
        });
        database.AppContainers.Add(new AppContainerEntry
        {
            Name = "container",
            Capabilities = ["internetClient"],
            ComAccessClsids = ["{00000000-0000-0000-0000-000000000000}"]
        });
        database.Settings.NagEligible = true;

        var snapshot = database.CreateSnapshot();

        database.Apps[0].Name = "Changed";
        database.Apps[0].PathPrefixes!.Add("D:\\More");
        database.Apps[0].ShortcutProtectionStates!.Add(
            new ShortcutProtectionState(@"C:\Links\Other.lnk", false, true, false));
        database.Apps[0].EnvironmentVariables!["K"] = "Changed";
        database.AppContainers[0].Capabilities!.Add("documentsLibrary");
        database.TrackingJobSids.Add("S-1-5-21-1-2-3-1002");

        Assert.Equal("App", snapshot.Apps[0].Name);
        Assert.Equal(["C:\\Allowed"], snapshot.Apps[0].PathPrefixes);
        Assert.Single(snapshot.Apps[0].ShortcutProtectionStates!);
        Assert.Equal(@"C:\Links\App.lnk", snapshot.Apps[0].ShortcutProtectionStates![0].ShortcutPath);
        Assert.Equal("V", snapshot.Apps[0].EnvironmentVariables!["K"]);
        Assert.Equal(["internetClient"], snapshot.AppContainers[0].Capabilities);
        Assert.True(snapshot.Settings.NagEligible);
        Assert.Equal(["S-1-5-21-1-2-3-1001"], snapshot.TrackingJobSids);
    }

    [Fact]
    public void ReplaceWithSnapshot_RestoresAllMutableProperties()
    {
        var original = new AppDatabase
        {
            Version = 9,
            LastPrefsFilePath = @"C:\Prefs\old.rfn",
            Settings = new AppSettings
            {
                LogVerbosity = LogVerbosity.Error,
                AutoLockInBackground = true,
                NagEligible = true
            },
            JobKeeperInstances = new Dictionary<string, JobKeeperInstanceIdentity>(StringComparer.OrdinalIgnoreCase)
            {
                ["sid|restricted"] = new JobKeeperInstanceIdentity
                {
                    TargetSid = "sid",
                    ExpectedMode = JobKeeperIntegrityMode.Restricted,
                    InstanceId = "inst-1",
                    PipeName = "pipe-1",
                    LastVerifiedKeeperPid = 123
                }
            },
            TrackingJobSids = ["S-1-5-21-1-2-3-2001"],
            AccountGroupSnapshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["sid"] = ["S-1-5-32-544"]
            },
            ShowSystemInRunAs = true
        };
        original.Apps.Add(new AppEntry { Id = "app1", Name = "App1", ExePath = @"C:\App1.exe" });
        original.Accounts.Add(new AccountEntry { Sid = "S-1-5-21-1-2-3-2001" });
        original.AppContainers.Add(new AppContainerEntry { Name = "container1" });
        original.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(
            new GrantedPathEntry { Path = @"C:\Shared", IsTraverseOnly = true });
        original.SidNames["S-1-5-21-1-2-3-2001"] = "User1";

        var snapshot = original.CreateSnapshot();

        var changed = new AppDatabase
        {
            Version = 1,
            LastPrefsFilePath = @"C:\Prefs\changed.rfn",
            Settings = new AppSettings { LogVerbosity = LogVerbosity.Debug },
        };
        changed.Apps.Add(new AppEntry { Id = "changed", Name = "Changed", ExePath = @"C:\Changed.exe" });
        changed.Accounts.Add(new AccountEntry { Sid = "changed-sid" });
        changed.AppContainers.Add(new AppContainerEntry { Name = "changed-container" });
        changed.GetOrCreateAccount(WellKnownSecuritySids.AllApplicationPackagesSid).Grants.Add(
            new GrantedPathEntry { Path = @"C:\ChangedShared", IsTraverseOnly = true });
        changed.SidNames["changed-sid"] = "ChangedUser";
        changed.ShowSystemInRunAs = false;

        changed.ReplaceWithSnapshot(snapshot);

        Assert.Equal(snapshot.Version, changed.Version);
        Assert.Equal(snapshot.LastPrefsFilePath, changed.LastPrefsFilePath);
        Assert.Equal(snapshot.Settings.LogVerbosity, changed.Settings.LogVerbosity);
        Assert.Equal(snapshot.Settings.AutoLockInBackground, changed.Settings.AutoLockInBackground);
        Assert.True(changed.Settings.NagEligible);
        Assert.Equal(snapshot.ShowSystemInRunAs, changed.ShowSystemInRunAs);
        Assert.Equal(snapshot.SidNames, changed.SidNames);
        Assert.Equal(snapshot.AccountGroupSnapshots!.Single().Value, changed.AccountGroupSnapshots!.Single().Value);
        Assert.Equal(snapshot.JobKeeperInstances!.Single().Value.InstanceId, changed.JobKeeperInstances!.Single().Value.InstanceId);
        Assert.Equal(snapshot.TrackingJobSids, changed.TrackingJobSids);

        Assert.Equal(snapshot.Apps.Single().Id, changed.Apps.Single().Id);
        Assert.Contains(changed.Accounts, account =>
            string.Equals(account.Sid, "S-1-5-21-1-2-3-2001", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(snapshot.AppContainers.Single().Name, changed.AppContainers.Single().Name);
        Assert.Equal(
            snapshot.GetAccount(WellKnownSecuritySids.AllApplicationPackagesSid)!.Grants.Single().Path,
            changed.GetAccount(WellKnownSecuritySids.AllApplicationPackagesSid)!.Grants.Single().Path);
    }
}
