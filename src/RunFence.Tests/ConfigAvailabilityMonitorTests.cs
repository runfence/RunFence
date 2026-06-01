using Moq;
using System.IO;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class ConfigAvailabilityMonitorTests
{
    private readonly Mock<IAppConfigService> _appConfigService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IAppStateProvider> _appStateProvider = new();
    private readonly Mock<IUiThreadInvoker> _uiThreadInvoker = new();
    private readonly OperationGuard _enforcementGuard = new();
    private readonly Mock<IUiTimerFactory> _timerFactory = new();
    private readonly Mock<IUiTimer> _timer = new();

    public ConfigAvailabilityMonitorTests()
    {
        _timerFactory.Setup(f => f.Create()).Returns(_timer.Object);

        // Permissive defaults: not shutting down, not modal, configs loaded.
        _appStateProvider.Setup(a => a.IsShuttingDown).Returns(false);
        _appStateProvider.Setup(a => a.IsModalOpen).Returns(false);
        _appConfigService.Setup(s => s.HasLoadedConfigs).Returns(true);
    }

    private ConfigAvailabilityMonitor BuildMonitor() =>
        new(_appConfigService.Object, _log.Object, _appStateProvider.Object,
            _uiThreadInvoker.Object, _enforcementGuard, _timerFactory.Object);

    [Fact]
    public void ScheduleAvailabilityCheck_DoesNotSchedule_WhenShuttingDown()
    {
        _appStateProvider.Setup(a => a.IsShuttingDown).Returns(true);

        using var monitor = BuildMonitor();
        monitor.ScheduleAvailabilityCheck();

        _appConfigService.Verify(s => s.GetLoadedConfigPaths(), Times.Never);
        _timerFactory.Verify(f => f.Create(), Times.Never);
    }

    [Fact]
    public void ScheduleAvailabilityCheck_DoesNotSchedule_WhenNoLoadedConfigs()
    {
        _appConfigService.Setup(s => s.HasLoadedConfigs).Returns(false);

        using var monitor = BuildMonitor();
        monitor.ScheduleAvailabilityCheck();

        _appConfigService.Verify(s => s.GetLoadedConfigPaths(), Times.Never);
        _timerFactory.Verify(f => f.Create(), Times.Never);
    }

    [Fact]
    public void ScheduleAvailabilityCheck_CreatesAndStartsTimer_WhenAllGuardsPass()
    {
        using var monitor = BuildMonitor();
        monitor.ScheduleAvailabilityCheck();

        _timerFactory.Verify(f => f.Create(), Times.Once);
        _timer.Verify(t => t.Start(), Times.Once);
    }

    [Fact]
    public void ScheduleAvailabilityCheck_DoesNotSchedule_WhenEnforcementInProgress()
    {
        _enforcementGuard.Begin();

        using var monitor = BuildMonitor();
        monitor.ScheduleAvailabilityCheck();

        _appConfigService.Verify(s => s.GetLoadedConfigPaths(), Times.Never);
        _timerFactory.Verify(f => f.Create(), Times.Never);

        _enforcementGuard.End();
    }

    [Fact]
    public void ScheduleAvailabilityCheck_DoesNotSchedule_WhenModalOpen()
    {
        _appStateProvider.Setup(a => a.IsModalOpen).Returns(true);

        using var monitor = BuildMonitor();
        monitor.ScheduleAvailabilityCheck();

        _timerFactory.Verify(f => f.Create(), Times.Never);
    }

    [Fact]
    public void ScheduleAvailabilityCheck_ReusesExistingTimer_OnSecondCall()
    {
        using var monitor = BuildMonitor();
        monitor.ScheduleAvailabilityCheck();
        monitor.ScheduleAvailabilityCheck();

        // Timer created only once, but started twice (re-armed)
        _timerFactory.Verify(f => f.Create(), Times.Once);
        _timer.Verify(t => t.Start(), Times.Exactly(2));
    }

    [Fact]
    public void Tick_ChecksConfigPaths_WhenGuardsPass()
    {
        _appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns(Array.Empty<string>());

        using var monitor = BuildMonitor();
        monitor.ScheduleAvailabilityCheck();
        _timer.Raise(t => t.Tick += null, _timer.Object, EventArgs.Empty);

        _appConfigService.Verify(s => s.GetLoadedConfigPaths(), Times.Once);
    }

    [Fact]
    public void Tick_SkipsAutoUnload_WhenMissingFileReappearsBeforeUiCallback()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"runfence-missing-{Guid.NewGuid():N}.rfn");
        _appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([missingPath]);

        try
        {
            List<string>? unloadedPaths = null;
            using var uiInvoker = new QueuedUiThreadInvoker();

            var manualTimerFactory = new ManualUiTimerFactory();
            using var monitor = new ConfigAvailabilityMonitor(
                _appConfigService.Object,
                _log.Object,
                _appStateProvider.Object,
                uiInvoker,
                _enforcementGuard,
                manualTimerFactory);
            monitor.AutoUnloadRequired += (_, paths) => unloadedPaths = paths;

            monitor.ScheduleAvailabilityCheck();
            var timer = Assert.Single(manualTimerFactory.Timers);
            timer.Fire();

            Assert.True(uiInvoker.WaitForPendingAction(TimeSpan.FromSeconds(2)), "BeginInvoke was not called within 2 seconds");

            File.WriteAllText(missingPath, "restored");
            uiInvoker.RunNext();

            Assert.Null(unloadedPaths);
        }
        finally
        {
            if (File.Exists(missingPath))
                File.Delete(missingPath);
        }
    }

    [Fact]
    public void Tick_RaisesAutoUnloadOnUiThread_WhenFilesMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"runfence-missing-{Guid.NewGuid():N}.rfn");
        _appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([missingPath]);

        List<string>? unloadedPaths = null;
        using var uiInvoker = new QueuedUiThreadInvoker();

        var manualTimerFactory = new ManualUiTimerFactory();
        using var monitor = new ConfigAvailabilityMonitor(
            _appConfigService.Object,
            _log.Object,
            _appStateProvider.Object,
            uiInvoker,
            _enforcementGuard,
            manualTimerFactory);
        monitor.AutoUnloadRequired += (_, paths) => unloadedPaths = paths;

        monitor.ScheduleAvailabilityCheck();
        var timer = Assert.Single(manualTimerFactory.Timers);
        timer.Fire();

        Assert.True(uiInvoker.WaitForPendingAction(TimeSpan.FromSeconds(2)), "BeginInvoke was not called within 2 seconds");
        uiInvoker.RunNext();

        Assert.Equal([missingPath], unloadedPaths);
    }

    [Fact]
    public void Tick_ReschedulesTimer_AfterAutoUnload()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"runfence-missing-{Guid.NewGuid():N}.rfn");
        _appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([missingPath]);
        using var uiInvoker = new QueuedUiThreadInvoker();

        using var monitor = new ConfigAvailabilityMonitor(
            _appConfigService.Object,
            _log.Object,
            _appStateProvider.Object,
            uiInvoker,
            _enforcementGuard,
            _timerFactory.Object);
        monitor.AutoUnloadRequired += (_, _) => { };

        monitor.ScheduleAvailabilityCheck();
        _timer.Raise(t => t.Tick += null, _timer.Object, EventArgs.Empty);

        Assert.True(uiInvoker.WaitForPendingAction(TimeSpan.FromSeconds(2)), "BeginInvoke was not called within 2 seconds");
        uiInvoker.RunNext();

        // After unload, ScheduleAvailabilityCheck is called -> timer restarted (Stop then Start again)
        _timer.Verify(t => t.Start(), Times.AtLeast(2));
    }

    private sealed class QueuedUiThreadInvoker : IUiThreadInvoker, IDisposable
    {
        private readonly Queue<Action> _actions = new();
        private readonly ManualResetEventSlim _actionQueued = new(false);
        private readonly object _sync = new();

        public T Invoke<T>(Func<T> func) => func();

        public void BeginInvoke(Action action)
        {
            lock (_sync)
            {
                _actions.Enqueue(action);
                _actionQueued.Set();
            }
        }

        public bool WaitForPendingAction(TimeSpan timeout) => _actionQueued.Wait(timeout);

        public void RunNext()
        {
            Action action;
            lock (_sync)
            {
                action = _actions.Dequeue();
                if (_actions.Count == 0)
                    _actionQueued.Reset();
            }

            action();
        }

        public void Dispose() => _actionQueued.Dispose();
    }
}
