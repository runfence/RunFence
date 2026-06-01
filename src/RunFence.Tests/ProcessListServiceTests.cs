using RunFence.Account;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Moq;
using Xunit;

namespace RunFence.Tests;

public sealed class ProcessListServiceTests
{
    private const string TrackingSid = "S-1-5-18";
    private const string RegularSid = "S-1-5-21-111-222-333-444";
    private const string ContainerSid = "S-1-15-2-1111-2222-3333-4444";

    private readonly Mock<ILoggingService> _log = new();

    [Fact]
    public void GetProcessesForSid_PersistedTrackingSid_ReturnsOnlyReopenedTrackedProcesses()
    {
        var snapshotSource = new FakeProcessSnapshotSource
        {
            ProcessIds = [11, 12, 99],
            TokenSids =
            {
                [11] = TrackingSid,
                [12] = TrackingSid,
                [99] = RegularSid,
            },
            ProcessInfos =
            {
                [11] = new ProcessInfo(11, @"C:\Tracked\reopened.exe", "reopened"),
                [12] = new ProcessInfo(12, @"C:\Tracked\not-reopened.exe", "not-reopened"),
            },
        };
        var processJobManager = new FakeProcessJobManager();
        processJobManager.SetMembers(TrackingSid, reopenPersistedJob: true, [11]);
        processJobManager.SetMembers(TrackingSid, reopenPersistedJob: false, [12]);
        var service = CreateService(snapshotSource, processJobManager, new FakeTrackingJobStateStore(TrackingSid));

        var processes = service.GetProcessesForSid(TrackingSid);

        var process = Assert.Single(processes);
        Assert.Equal(11, process.Pid);
        Assert.Equal(@"C:\Tracked\reopened.exe", process.ExecutablePath);
    }

    [Fact]
    public void GetProcessesForSid_UnpersistedTrackingSid_ReturnsOnlyNonReopenedTrackedProcesses()
    {
        var snapshotSource = new FakeProcessSnapshotSource
        {
            ProcessIds = [11, 12],
            TokenSids =
            {
                [11] = TrackingSid,
                [12] = TrackingSid,
            },
            ProcessInfos =
            {
                [11] = new ProcessInfo(11, @"C:\Tracked\reopened.exe", "reopened"),
                [12] = new ProcessInfo(12, @"C:\Tracked\not-reopened.exe", "not-reopened"),
            },
        };
        var processJobManager = new FakeProcessJobManager();
        processJobManager.SetMembers(TrackingSid, reopenPersistedJob: true, [11]);
        processJobManager.SetMembers(TrackingSid, reopenPersistedJob: false, [12]);
        var service = CreateService(snapshotSource, processJobManager, new FakeTrackingJobStateStore());

        var processes = service.GetProcessesForSid(TrackingSid);

        var process = Assert.Single(processes);
        Assert.Equal(12, process.Pid);
        Assert.Equal(@"C:\Tracked\not-reopened.exe", process.ExecutablePath);
    }

    [Fact]
    public void GetSidsWithProcesses_PersistedTrackingSid_UsesReopenedMembershipAndIncludesContainerSids()
    {
        var snapshotSource = new FakeProcessSnapshotSource
        {
            ProcessIds = [21, 22, 31],
            TokenSids =
            {
                [21] = TrackingSid,
                [22] = RegularSid,
                [31] = RegularSid,
            },
            AppContainerSids = { [31] = ContainerSid },
        };
        var processJobManager = new FakeProcessJobManager();
        processJobManager.SetMembers(TrackingSid, reopenPersistedJob: true, [21]);
        processJobManager.SetMembers(TrackingSid, reopenPersistedJob: false, [22]);
        var service = CreateService(snapshotSource, processJobManager, new FakeTrackingJobStateStore(TrackingSid));

        var sids = service.GetSidsWithProcesses([TrackingSid, ContainerSid]);

        Assert.Equal(2, sids.Count);
        Assert.Contains(TrackingSid, sids);
        Assert.Contains(ContainerSid, sids);
    }

    [Fact]
    public void GetSidsWithProcesses_UnpersistedTrackingSid_UsesNonReopenedMembership()
    {
        var snapshotSource = new FakeProcessSnapshotSource
        {
            ProcessIds = [21, 22],
            TokenSids =
            {
                [21] = RegularSid,
                [22] = TrackingSid,
            },
        };
        var processJobManager = new FakeProcessJobManager();
        processJobManager.SetMembers(TrackingSid, reopenPersistedJob: true, [21]);
        processJobManager.SetMembers(TrackingSid, reopenPersistedJob: false, [22]);
        var service = CreateService(snapshotSource, processJobManager, new FakeTrackingJobStateStore());

        var sids = service.GetSidsWithProcesses([TrackingSid]);

        var foundSid = Assert.Single(sids);
        Assert.Equal(TrackingSid, foundSid);
    }

    [Fact]
    public void GetProcessesForSid_CancellationStopsEnumeration()
    {
        using var cancellation = new CancellationTokenSource();
        var snapshotSource = new FakeProcessSnapshotSource
        {
            ProcessIds = [41, 42],
            TokenSids =
            {
                [41] = RegularSid,
                [42] = RegularSid,
            },
            ProcessInfos =
            {
                [41] = new ProcessInfo(41, @"C:\Apps\first.exe", "first"),
                [42] = new ProcessInfo(42, @"C:\Apps\second.exe", "second"),
            },
            OnGetTokenSid = _ => cancellation.Cancel(),
        };
        var service = CreateService(snapshotSource, new FakeProcessJobManager(), new FakeTrackingJobStateStore());

        Assert.Throws<OperationCanceledException>(() => service.GetProcessesForSid(RegularSid, cancellation.Token));
        Assert.Equal([41], snapshotSource.CollectedProcessIds);
    }

    private ProcessListService CreateService(
        IProcessSnapshotSource snapshotSource,
        IProcessJobManager processJobManager,
        ITrackingJobStateStore trackingJobStateStore) =>
        new(_log.Object, snapshotSource, processJobManager, () => trackingJobStateStore);

    private sealed class FakeProcessSnapshotSource : IProcessSnapshotSource
    {
        public IReadOnlyList<int> ProcessIds { get; init; } = [];
        public Dictionary<int, string> TokenSids { get; } = new();
        public Dictionary<int, string> AppContainerSids { get; } = new();
        public Dictionary<int, bool> ExitedStates { get; } = new();
        public Dictionary<int, ProcessInfo> ProcessInfos { get; } = new();
        public List<int> CollectedProcessIds { get; } = [];
        public Action<int>? OnGetTokenSid { get; init; }

        public IReadOnlyList<int> GetProcessIds() => ProcessIds;

        public string? GetTokenSid(int pid, int tokenInfoClass)
        {
            CollectedProcessIds.Add(pid);
            OnGetTokenSid?.Invoke(pid);
            return TokenSids.GetValueOrDefault(pid);
        }

        public string? GetAppContainerSid(int pid) => AppContainerSids.GetValueOrDefault(pid);

        public bool HasExited(int pid) => ExitedStates.GetValueOrDefault(pid);

        public ProcessInfo? ReadProcessInfo(int pid) => ProcessInfos.GetValueOrDefault(pid);
    }

    private sealed class FakeProcessJobManager : IProcessJobManager
    {
        private readonly Dictionary<(string Sid, bool ReopenPersistedJob), HashSet<int>> _members = new();

        public JobAssignmentResult TryAssignToJob(string sid, IntPtr hProcess, JobAssignment assignment, string? jobNameOverride = null) =>
            throw new NotSupportedException();

        public HashSet<int>? GetJobMembers(string sid, bool reopenPersistedJob) =>
            _members.TryGetValue((sid, reopenPersistedJob), out var members)
                ? new HashSet<int>(members)
                : [];

        public HashSet<int>? GetKeeperJobMembers(string sid, bool isLow) =>
            throw new NotSupportedException();

        public void RegisterVerifiedRestrictedJob(string sid, bool isLow, IntPtr jobHandle) =>
            throw new NotSupportedException();

        public void ResetJobHandle(string sid, JobAssignment assignment) =>
            throw new NotSupportedException();

        public void SetMembers(string sid, bool reopenPersistedJob, params int[] pids) =>
            _members[(sid, reopenPersistedJob)] = pids.ToHashSet();
    }

    private sealed class FakeTrackingJobStateStore(params string[] trackedSids) : ITrackingJobStateStore
    {
        private readonly HashSet<string> _trackedSids = trackedSids.ToHashSet(StringComparer.OrdinalIgnoreCase);

        public bool ContainsTrackingJobSid(string sid) => _trackedSids.Contains(sid);

        public void AddTrackingJobSid(string sid) =>
            throw new NotSupportedException();

        public void RemoveTrackingJobSid(string sid, bool saveImmediately = true) =>
            throw new NotSupportedException();

        public void MigrateTrackingJobSid(string oldSid, string newSid, bool saveImmediately = true) =>
            throw new NotSupportedException();
    }
}
