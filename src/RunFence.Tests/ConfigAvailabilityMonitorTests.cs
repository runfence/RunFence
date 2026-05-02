using Moq;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Persistence.UI;
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
    public void Tick_StopsTimer_BeforeCheckingPaths()
    {
        _appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns(Array.Empty<string>());

        using var monitor = BuildMonitor();
        monitor.ScheduleAvailabilityCheck();

        // Fire the timer Tick event
        _timer.Raise(t => t.Tick += null, _timer.Object, EventArgs.Empty);

        _timer.Verify(t => t.Stop(), Times.AtLeastOnce);
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
    public void Tick_RaisesAutoUnloadOnUiThread_WhenFilesMissing()
    {
        var missingPath = @"C:\does-not-exist-runfence-test.rfn";
        _appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([missingPath]);

        // Use ManualResetEventSlim to avoid Thread.Sleep polling: signal when BeginInvoke fires.
        var callbackSignal = new ManualResetEventSlim(false);
        Action? capturedAction = null;
        _uiThreadInvoker.Setup(u => u.BeginInvoke(It.IsAny<Action>()))
            .Callback<Action>(a =>
            {
                capturedAction = a;
                callbackSignal.Set();
            });

        List<string>? unloadedPaths = null;

        using var monitor = BuildMonitor();
        monitor.AutoUnloadRequired += (_, paths) => unloadedPaths = paths;

        monitor.ScheduleAvailabilityCheck();
        _timer.Raise(t => t.Tick += null, _timer.Object, EventArgs.Empty);

        // Wait for Task.Run to post the UI thread callback (no polling — event-driven)
        Assert.True(callbackSignal.Wait(TimeSpan.FromSeconds(2)), "BeginInvoke was not called within 2 seconds");

        capturedAction!();

        Assert.NotNull(unloadedPaths);
        Assert.Contains(missingPath, unloadedPaths!);
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Auto-unloading"))), Times.Once);
    }

    [Fact]
    public void Tick_ReschedulesTimer_AfterAutoUnload()
    {
        var missingPath = @"C:\does-not-exist-runfence-test2.rfn";
        _appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([missingPath]);

        var callbackSignal = new ManualResetEventSlim(false);
        Action? capturedAction = null;
        _uiThreadInvoker.Setup(u => u.BeginInvoke(It.IsAny<Action>()))
            .Callback<Action>(a =>
            {
                capturedAction = a;
                callbackSignal.Set();
            });

        using var monitor = BuildMonitor();
        monitor.AutoUnloadRequired += (_, _) => { };

        monitor.ScheduleAvailabilityCheck();
        _timer.Raise(t => t.Tick += null, _timer.Object, EventArgs.Empty);

        Assert.True(callbackSignal.Wait(TimeSpan.FromSeconds(2)), "BeginInvoke was not called within 2 seconds");
        capturedAction!();

        // After unload, ScheduleAvailabilityCheck is called → timer restarted (Stop then Start again)
        _timer.Verify(t => t.Start(), Times.AtLeast(2));
    }
}
