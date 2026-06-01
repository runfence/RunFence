using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public sealed class SessionTrackingJobStateStoreTests
{
    [Fact]
    public void ContainsTrackingJobSid_IsCaseInsensitive_AndUsesUiInvoker()
    {
        var harness = CreateHarness(["S-1-5-21-Tracked"]);

        var result = harness.Store.ContainsTrackingJobSid("s-1-5-21-tracked");

        Assert.True(result);
        Assert.Equal(1, harness.UiInvoker.InvokeCallCount);
        Assert.Equal(0, harness.SessionSaver.SaveConfigCallCount);
    }

    [Fact]
    public void AddTrackingJobSid_IsIdempotent_AndUsesUiInvoker()
    {
        var harness = CreateHarness(["S-1-5-21-Tracked"]);

        harness.Store.AddTrackingJobSid("s-1-5-21-tracked");
        harness.Store.AddTrackingJobSid("S-1-5-21-New");

        Assert.Equal(["S-1-5-21-Tracked", "S-1-5-21-New"], harness.Session.Database.TrackingJobSids);
        Assert.Equal(2, harness.UiInvoker.InvokeCallCount);
        Assert.Equal(1, harness.SessionSaver.SaveConfigCallCount);
    }

    [Fact]
    public void RemoveTrackingJobSid_RemovesCaseInsensitiveMatch_CleansUpNullList_AndUsesUiInvoker()
    {
        var harness = CreateHarness(["S-1-5-21-Tracked"]);

        harness.Store.RemoveTrackingJobSid("s-1-5-21-tracked");

        Assert.Null(harness.Session.Database.TrackingJobSids);
        Assert.Equal(1, harness.UiInvoker.InvokeCallCount);
        Assert.Equal(1, harness.SessionSaver.SaveConfigCallCount);
    }

    [Fact]
    public void RemoveTrackingJobSid_SaveImmediatelyFalse_DoesNotSave()
    {
        var harness = CreateHarness(["S-1-5-21-Tracked", "S-1-5-21-Other"]);

        harness.Store.RemoveTrackingJobSid("S-1-5-21-Tracked", saveImmediately: false);

        Assert.Equal(["S-1-5-21-Other"], harness.Session.Database.TrackingJobSids);
        Assert.Equal(1, harness.UiInvoker.InvokeCallCount);
        Assert.Equal(0, harness.SessionSaver.SaveConfigCallCount);
    }

    [Fact]
    public void MigrateTrackingJobSid_TargetAlreadyExists_RemovesSourceWithoutDuplicate_AndUsesUiInvoker()
    {
        var harness = CreateHarness(["S-1-5-21-Old", "S-1-5-21-New"]);

        harness.Store.MigrateTrackingJobSid("s-1-5-21-old", "S-1-5-21-New");

        Assert.Equal(["S-1-5-21-New"], harness.Session.Database.TrackingJobSids);
        Assert.Equal(1, harness.UiInvoker.InvokeCallCount);
        Assert.Equal(1, harness.SessionSaver.SaveConfigCallCount);
    }

    [Fact]
    public void MigrateTrackingJobSid_SaveImmediatelyFalse_DoesNotSave()
    {
        var harness = CreateHarness(["S-1-5-21-Old"]);

        harness.Store.MigrateTrackingJobSid("S-1-5-21-Old", "S-1-5-21-New", saveImmediately: false);

        Assert.Equal(["S-1-5-21-New"], harness.Session.Database.TrackingJobSids);
        Assert.Equal(1, harness.UiInvoker.InvokeCallCount);
        Assert.Equal(0, harness.SessionSaver.SaveConfigCallCount);
    }

    [Fact]
    public void BlankSidOperations_AreNoOps_ButStillExecuteThroughUiInvoker()
    {
        var harness = CreateHarness(["S-1-5-21-Tracked"]);

        Assert.False(harness.Store.ContainsTrackingJobSid(" "));
        harness.Store.AddTrackingJobSid("");
        harness.Store.RemoveTrackingJobSid(" ");
        harness.Store.MigrateTrackingJobSid("", "S-1-5-21-New");

        Assert.Equal(["S-1-5-21-Tracked"], harness.Session.Database.TrackingJobSids);
        Assert.Equal(4, harness.UiInvoker.InvokeCallCount);
        Assert.Equal(0, harness.SessionSaver.SaveConfigCallCount);
    }

    private static Harness CreateHarness(List<string>? trackingJobSids = null)
    {
        var session = new SessionContext
        {
            Database = new AppDatabase
            {
                TrackingJobSids = trackingJobSids
            }
        };
        var uiInvoker = new RecordingUiThreadInvoker();
        var sessionSaver = new RecordingSessionSaver();
        var store = new SessionTrackingJobStateStore(
            new LambdaSessionProvider(() => session),
            () => uiInvoker,
            sessionSaver);

        return new Harness(session, store, uiInvoker, sessionSaver);
    }

    private sealed record Harness(
        SessionContext Session,
        SessionTrackingJobStateStore Store,
        RecordingUiThreadInvoker UiInvoker,
        RecordingSessionSaver SessionSaver);

    private sealed class RecordingUiThreadInvoker : IUiThreadInvoker
    {
        public int InvokeCallCount { get; private set; }

        public T Invoke<T>(Func<T> func)
        {
            InvokeCallCount++;
            return func();
        }

        public void BeginInvoke(Action action) => action();
    }

    private sealed class RecordingSessionSaver : ISessionSaver
    {
        public int SaveConfigCallCount { get; private set; }

        public void SaveConfig() => SaveConfigCallCount++;
    }
}
