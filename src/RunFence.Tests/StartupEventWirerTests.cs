using Microsoft.Win32;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge;
using RunFence.ForegroundMarker;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Security;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public class StartupEventWirerTests
{
    [Fact]
    public void DataRefresh_WiresRefreshTrayWarningAndEphemeralNotifications()
    {
        var uiThreadInvoker = new RecordingUiThreadInvoker();
        var mainForm = new RecordingMainFormTarget();
        var configManagement = new RecordingConfigManagementSource();
        var accountSource = new RecordingEphemeralAccountSource();
        var containerSource = new RecordingEphemeralContainerSource();
        var appDataSource = new RecordingApplicationDataSource();
        var dataChangeNotifier = new RecordingDataChangeNotifier();
        var warningPresenter = new RecordingReencryptionWarningPresenter();
        var wirer = new DataRefreshStartupEventWirer(
            uiThreadInvoker,
            mainForm,
            configManagement,
            accountSource,
            containerSource,
            appDataSource,
            dataChangeNotifier,
            warningPresenter);

        wirer.WireEvents();
        accountSource.RaiseAccountsChanged();
        containerSource.RaiseContainersChanged();
        configManagement.RaiseDataRefreshRequested();
        configManagement.RaiseTrayUpdateRequested();
        appDataSource.RaiseDataChanged();
        configManagement.RaiseReencryptionWarning("reencrypt");

        Assert.Equal(5, uiThreadInvoker.BeginInvokeCallCount);
        Assert.Equal(0, uiThreadInvoker.InvokeCallCount);
        Assert.Equal(2, dataChangeNotifier.NotifyDataChangedCount);
        Assert.Equal(1, mainForm.SetDataCount);
        Assert.Equal(1, mainForm.UpdateTrayCount);
        Assert.Equal(1, mainForm.HandleDataChangedCount);
        Assert.Equal("reencrypt", warningPresenter.LastMessage);
    }

    [Fact]
    public void LockUi_WiresWindowRequestsAndWindowsHelloConfirmations()
    {
        var uiThreadInvoker = new RecordingUiThreadInvoker();
        var lockManager = new RecordingLockUiEventSource();
        var fallbackPrompt = new RecordingWindowsHelloPinFallbackPromptEventSource();
        var mainForm = new RecordingMainFormTarget
        {
            WindowsHelloUnavailableResult = true,
            WindowsHelloFailedResult = false
        };
        var wirer = new LockUiStartupEventWirer(uiThreadInvoker, lockManager, fallbackPrompt, mainForm);

        wirer.WireEvents();

        lockManager.RaiseShowWindowRequested();
        lockManager.RaiseShowWindowUnlockedRequested();
        lockManager.RaiseWindowlessUnlockCompleted();
        var unavailableResult = fallbackPrompt.RaiseWindowsHelloUnavailableConfirmRequested();
        var failedResult = fallbackPrompt.RaiseWindowsHelloFailedConfirmRequested();

        Assert.Equal(3, uiThreadInvoker.BeginInvokeCallCount);
        Assert.Equal(2, uiThreadInvoker.InvokeCallCount);
        Assert.Equal(1, mainForm.ShowWindowNormalCount);
        Assert.Equal(1, mainForm.ShowWindowUnlockedCount);
        Assert.Equal(1, mainForm.HandleWindowlessUnlockCount);
        Assert.Equal(1, mainForm.ConfirmWindowsHelloUnavailableCount);
        Assert.Equal(1, mainForm.ConfirmWindowsHelloFailedCount);
        Assert.True(unavailableResult);
        Assert.False(failedResult);
    }

    [Fact]
    public void Wizard_WiresButtonRequestAndCompletionRefresh()
    {
        var source = new RecordingWizardRequestSource();
        var launcher = new RecordingWizardLauncher();
        var mainForm = new RecordingMainFormTarget();
        var owner = new Mock<IWin32Window>().Object;
        var wirer = new WizardStartupEventWirer(source, launcher, mainForm);

        wirer.WireEvents();
        source.RaiseWizardRequested(owner);
        launcher.RaiseWizardCompleted();

        Assert.True(source.WizardButtonEnabled);
        Assert.Equal(1, launcher.OpenWizardCount);
        Assert.Same(owner, launcher.LastOwner);
        Assert.Equal(1, mainForm.HandleDataChangedCount);
    }

    [Fact]
    public void DragBridge_WiresDataSettingsAndDispose()
    {
        var uiThreadInvoker = new RecordingUiThreadInvoker();
        var lifetime = new RecordingFormLifetime();
        var applicationDataSource = new RecordingApplicationDataSource();
        var settingsChangeSource = new RecordingDragBridgeSettingsChangeSource();
        using var session = new SessionContext
        {
            Database = new AppDatabase
            {
                Settings = new AppSettings()
            },
            CredentialStore = new CredentialStore()
        };
        var dragBridgeService = new Mock<IDragBridgeService>();
        var wirer = new DragBridgeEventWirer(
            uiThreadInvoker,
            lifetime,
            applicationDataSource,
            new LambdaSessionProvider(() => session),
            settingsChangeSource,
            dragBridgeService.Object);

        wirer.WireEvents();
        applicationDataSource.RaiseDataChanged();
        settingsChangeSource.RaiseDragBridgeSettingsChanged();
        lifetime.RaiseFormClosed();

        Assert.Equal(1, uiThreadInvoker.BeginInvokeCallCount);
        Assert.Equal(0, uiThreadInvoker.InvokeCallCount);
        dragBridgeService.Verify(service => service.SetData(session), Times.Once);
        dragBridgeService.Verify(service => service.ApplySettings(session.Database.Settings), Times.Once);
        dragBridgeService.Verify(service => service.Dispose(), Times.Once);
    }

    [Fact]
    public void SessionSwitch_InvalidatesInteractiveDesktopCacheForHandledReasonsAndUnsubscribesOnClose()
    {
        var source = new RecordingSessionSwitchEventSource();
        var lifetime = new RecordingFormLifetime();
        var sidCache = new Mock<IInteractiveUserSidCache>();
        var desktopProvider = new Mock<IInteractiveUserDesktopProvider>();
        var coordinator = new InteractiveUserRefreshCoordinator(sidCache.Object, desktopProvider.Object);
        var refreshCount = 0;
        coordinator.InteractiveUserRefreshed += () => refreshCount++;
        var wirer = new SessionSwitchStartupEventWirer(source, lifetime, coordinator);

        wirer.WireEvents();
        source.Raise(SessionSwitchReason.SessionLock);
        source.Raise(SessionSwitchReason.ConsoleConnect);
        lifetime.RaiseFormClosed();
        source.Raise(SessionSwitchReason.ConsoleConnect);

        sidCache.Verify(cache => cache.ReinitializeInteractiveUserSid(), Times.Once);
        desktopProvider.Verify(d => d.InvalidateCache(), Times.Once);
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public void ForegroundPrivilegeMarkerStartup_StartsOnlyAfterHandleCreated_AndOnlyOnce()
    {
        var startupHost = new RecordingStartupIpcHost();
        var appStateProvider = new Mock<IAppStateProvider>();
        appStateProvider.SetupGet(provider => provider.Database)
            .Returns(new AppDatabase
            {
                Settings = new AppSettings
                {
                ShowForegroundPrivilegeMarker = false,
                ShowForegroundPrivilegeMarkerWhenFullscreen = true
                }
            });
        var markerService = new Mock<IForegroundPrivilegeMarkerService>();
        var log = new Mock<ILoggingService>();
        var trayWarningSink = new RecordingTrayWarningSink();
        var wirer = new ForegroundPrivilegeMarkerStartupEventWirer(
            startupHost,
            appStateProvider.Object,
            markerService.Object,
            log.Object,
            trayWarningSink);

        wirer.WireEvents();

        markerService.Verify(service => service.Start(It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        startupHost.RaiseHandleCreated();
        startupHost.RaiseHandleCreated();

        markerService.Verify(service => service.Start(false, true), Times.Once);
    }

    [Fact]
    public void ForegroundPrivilegeMarkerStartup_WireEvents_DoesNotRegisterDuplicateHandlers()
    {
        var startupHost = new RecordingStartupIpcHost();
        var appStateProvider = new Mock<IAppStateProvider>();
        appStateProvider.SetupGet(provider => provider.Database)
            .Returns(new AppDatabase
            {
                Settings = new AppSettings()
            });
        var markerService = new Mock<IForegroundPrivilegeMarkerService>();
        var log = new Mock<ILoggingService>();
        var trayWarningSink = new RecordingTrayWarningSink();
        var wirer = new ForegroundPrivilegeMarkerStartupEventWirer(
            startupHost,
            appStateProvider.Object,
            markerService.Object,
            log.Object,
            trayWarningSink);

        wirer.WireEvents();
        wirer.WireEvents();
        startupHost.RaiseHandleCreated();

        markerService.Verify(service => service.Start(true, true), Times.Once);
    }

    [Fact]
    public void ForegroundPrivilegeMarkerStartup_StartFailure_IsSwallowedAndStopsRetrying()
    {
        var startupHost = new RecordingStartupIpcHost();
        var settings = new AppSettings
        {
            ShowForegroundPrivilegeMarker = true,
            ShowForegroundPrivilegeMarkerWhenFullscreen = false
        };
        var appStateProvider = new Mock<IAppStateProvider>();
        appStateProvider.SetupGet(provider => provider.Database)
            .Returns(new AppDatabase
            {
                Settings = settings
            });
        var markerService = new Mock<IForegroundPrivilegeMarkerService>();
        var callCount = 0;
        markerService.Setup(service => service.Start(true, false))
            .Callback(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("transient startup failure");
            });
        markerService.Setup(service => service.Stop());
        var log = new Mock<ILoggingService>();
        var trayWarningSink = new RecordingTrayWarningSink();
        var wirer = new ForegroundPrivilegeMarkerStartupEventWirer(
            startupHost,
            appStateProvider.Object,
            markerService.Object,
            log.Object,
            trayWarningSink);

        wirer.WireEvents();

        startupHost.RaiseHandleCreated();
        startupHost.RaiseHandleCreated();

        wirer.WireEvents();
        wirer.WireEvents();
        startupHost.RaiseHandleCreated();

        markerService.Verify(service => service.Start(true, false), Times.Once);
        markerService.Verify(service => service.Stop(), Times.Once);
        log.Verify(l => l.Warn("Foreground privilege marker startup failed: transient startup failure"), Times.Once);
        Assert.Equal(1, callCount);
        Assert.True(trayWarningSink.ShowWarningCalled);
        Assert.True(settings.ShowForegroundPrivilegeMarker);
    }

    [Fact]
    public void ForegroundPrivilegeMarkerStartup_FailedStartup_RequestFalseStillStopsForCleanup()
    {
        var startupHost = new RecordingStartupIpcHost();
        var appStateProvider = new Mock<IAppStateProvider>();
        var settings = new AppSettings
        {
            ShowForegroundPrivilegeMarker = false,
            ShowForegroundPrivilegeMarkerWhenFullscreen = true
        };
        appStateProvider.SetupGet(provider => provider.Database)
            .Returns(new AppDatabase
            {
                Settings = settings
            });
        var markerService = new Mock<IForegroundPrivilegeMarkerService>();
        markerService.Setup(service => service.Start(false, true))
            .Throws(new InvalidOperationException("disabled startup failure"));
        markerService.Setup(service => service.Stop());
        var log = new Mock<ILoggingService>();
        var trayWarningSink = new RecordingTrayWarningSink();
        var wirer = new ForegroundPrivilegeMarkerStartupEventWirer(
            startupHost,
            appStateProvider.Object,
            markerService.Object,
            log.Object,
            trayWarningSink);

        wirer.WireEvents();
        startupHost.RaiseHandleCreated();

        markerService.Verify(service => service.Start(false, true), Times.Once);
        markerService.Verify(service => service.Stop(), Times.Once);
        markerService.Verify(service => service.Start(true, It.IsAny<bool>()), Times.Never);
        Assert.True(trayWarningSink.ShowWarningCalled);
        Assert.False(settings.ShowForegroundPrivilegeMarker);
        log.Verify(l => l.Warn("Foreground privilege marker startup failed: disabled startup failure"), Times.Once);
    }

    [Fact]
    public void ForegroundPrivilegeMarkerStartup_WarningsAndDisableFailuresAreSwallowed()
    {
        var startupHost = new RecordingStartupIpcHost();
        var settings = new AppSettings
        {
            ShowForegroundPrivilegeMarker = true,
            ShowForegroundPrivilegeMarkerWhenFullscreen = false
        };
        var appStateProvider = new Mock<IAppStateProvider>();
        appStateProvider.SetupGet(provider => provider.Database)
            .Returns(new AppDatabase
            {
                Settings = settings
            });

        var markerService = new Mock<IForegroundPrivilegeMarkerService>();
        markerService.Setup(service => service.Start(true, false))
            .Throws(new InvalidOperationException("startup failed"));
        markerService.Setup(service => service.Stop())
            .Throws(new InvalidOperationException("stop failed"));

        var log = new Mock<ILoggingService>();
        log.Setup(l => l.Warn(It.IsAny<string>())).Throws(new Exception("logger failed"));

        var trayWarningSink = new Mock<ITrayWarningSink>();
        trayWarningSink.Setup(t => t.ShowWarning(It.IsAny<string>())).Throws(new Exception("sink failed"));

        var wirer = new ForegroundPrivilegeMarkerStartupEventWirer(
            startupHost,
            appStateProvider.Object,
            markerService.Object,
            log.Object,
            trayWarningSink.Object);

        wirer.WireEvents();
        startupHost.RaiseHandleCreated();

        markerService.Verify(service => service.Start(true, false), Times.Once);
        markerService.Verify(service => service.Stop(), Times.Once);
        trayWarningSink.Verify(t => t.ShowWarning(It.Is<string>(text => text.Contains("Foreground privilege marker failed"))), Times.Once);
        log.Verify(l => l.Warn(It.IsAny<string>()), Times.AtLeast(3));
        Assert.True(settings.ShowForegroundPrivilegeMarker);
    }

    private sealed class RecordingMainFormTarget : IMainFormDataRefreshTarget, IMainFormLockTarget
    {
        public int SetDataCount { get; private set; }
        public int UpdateTrayCount { get; private set; }
        public int HandleDataChangedCount { get; private set; }
        public int ShowWindowNormalCount { get; private set; }
        public int ShowWindowUnlockedCount { get; private set; }
        public int HandleWindowlessUnlockCount { get; private set; }
        public int ConfirmWindowsHelloUnavailableCount { get; private set; }
        public int ConfirmWindowsHelloFailedCount { get; private set; }
        public bool WindowsHelloUnavailableResult { get; init; }
        public bool WindowsHelloFailedResult { get; init; }

        public void SetData() => SetDataCount++;
        public void UpdateTray() => UpdateTrayCount++;
        public void HandleDataChanged() => HandleDataChangedCount++;
        public void ShowWindowNormal() => ShowWindowNormalCount++;
        public void ShowWindowUnlocked() => ShowWindowUnlockedCount++;
        public void HandleWindowlessUnlock() => HandleWindowlessUnlockCount++;
        public bool ConfirmWindowsHelloUnavailableFallback()
        {
            ConfirmWindowsHelloUnavailableCount++;
            return WindowsHelloUnavailableResult;
        }

        public bool ConfirmWindowsHelloFailedFallback()
        {
            ConfirmWindowsHelloFailedCount++;
            return WindowsHelloFailedResult;
        }
    }

    private sealed class RecordingConfigManagementSource : IConfigManagementEventSource
    {
        public event Action? DataRefreshRequested;
        public event Action? TrayUpdateRequested;
        public event Action<string>? ReencryptionWarning;
        public void RaiseDataRefreshRequested() => DataRefreshRequested?.Invoke();
        public void RaiseTrayUpdateRequested() => TrayUpdateRequested?.Invoke();
        public void RaiseReencryptionWarning(string message) => ReencryptionWarning?.Invoke(message);
    }

    private sealed class RecordingEphemeralAccountSource : IEphemeralAccountChangeSource
    {
        public event Action? AccountsChanged;
        public void RaiseAccountsChanged() => AccountsChanged?.Invoke();
    }

    private sealed class RecordingEphemeralContainerSource : IEphemeralContainerChangeSource
    {
        public event Action? ContainersChanged;
        public void RaiseContainersChanged() => ContainersChanged?.Invoke();
    }

    private sealed class RecordingApplicationDataSource : IApplicationDataChangeSource
    {
        public event Action? DataChanged;
        public void RaiseDataChanged() => DataChanged?.Invoke();
    }

    private sealed class RecordingReencryptionWarningPresenter : IReencryptionWarningPresenter
    {
        public string? LastMessage { get; private set; }
        public void ShowWarning(string message) => LastMessage = message;
    }

    private sealed class RecordingDataChangeNotifier : IDataChangeNotifier
    {
        public int NotifyDataChangedCount { get; private set; }
        public void NotifyDataChanged() => NotifyDataChangedCount++;
    }

    private sealed class RecordingTrayWarningSink : ITrayWarningSink
    {
        public bool ShowWarningCalled { get; private set; }
        public string? LastWarning { get; private set; }

        public void ShowWarning(string text)
        {
            ShowWarningCalled = true;
            LastWarning = text;
        }
    }

    private sealed class RecordingLockUiEventSource : ILockUiEventSource
    {
        public event Action? ShowWindowRequested;
        public event Action? ShowWindowUnlockedRequested;
        public event Action? WindowlessUnlockCompleted;
        public void RaiseShowWindowRequested() => ShowWindowRequested?.Invoke();
        public void RaiseShowWindowUnlockedRequested() => ShowWindowUnlockedRequested?.Invoke();
        public void RaiseWindowlessUnlockCompleted() => WindowlessUnlockCompleted?.Invoke();
    }

    private sealed class RecordingWindowsHelloPinFallbackPromptEventSource : IWindowsHelloPinFallbackPromptEventSource
    {
        public event Func<bool>? WindowsHelloUnavailableConfirmRequested;
        public event Func<bool>? WindowsHelloFailedConfirmRequested;

        public bool RaiseWindowsHelloUnavailableConfirmRequested() =>
            WindowsHelloUnavailableConfirmRequested?.Invoke() ?? false;
        public bool RaiseWindowsHelloFailedConfirmRequested() =>
            WindowsHelloFailedConfirmRequested?.Invoke() ?? false;
    }

    private sealed class RecordingWizardRequestSource : IWizardRequestSource
    {
        public event Action<IWin32Window>? WizardRequested;
        public bool WizardButtonEnabled { get; set; }
        public void RaiseWizardRequested(IWin32Window owner) => WizardRequested?.Invoke(owner);
    }

    private sealed class RecordingWizardLauncher : IWizardLauncher
    {
        public event Action? WizardCompleted;
        public int OpenWizardCount { get; private set; }
        public IWin32Window? LastOwner { get; private set; }
        public Task OpenWizardAsync(IWin32Window owner)
        {
            OpenWizardCount++;
            LastOwner = owner;
            return Task.CompletedTask;
        }

        public void RaiseWizardCompleted() => WizardCompleted?.Invoke();
    }

    private sealed class RecordingSessionSwitchEventSource : ISessionSwitchEventSource
    {
        public event SessionSwitchEventHandler? SessionSwitch;
        public void Raise(SessionSwitchReason reason) =>
            SessionSwitch?.Invoke(this, new SessionSwitchEventArgs(reason));
    }

    private sealed class RecordingFormLifetime : IStartupFormLifetime
    {
        public event FormClosedEventHandler? FormClosed;
        public void RaiseFormClosed() =>
            FormClosed?.Invoke(this, new FormClosedEventArgs(CloseReason.UserClosing));
    }

    private sealed class RecordingStartupIpcHost : IStartupIpcHost
    {
        public event EventHandler? HandleCreated;
        public event FormClosingEventHandler? FormClosing;

        public void RaiseHandleCreated() => HandleCreated?.Invoke(this, EventArgs.Empty);
        public void BeginInvokeOnUiThread(Action action) => action();
        public void SetStartupComplete()
        {
        }

        public void RaiseFormClosing() =>
            FormClosing?.Invoke(this, new FormClosingEventArgs(CloseReason.UserClosing, false));
    }

    private sealed class RecordingDragBridgeSettingsChangeSource : IDragBridgeSettingsChangeSource
    {
        public event Action? DragBridgeSettingsChanged;
        public void RaiseDragBridgeSettingsChanged() => DragBridgeSettingsChanged?.Invoke();
    }

    private sealed class RecordingUiThreadInvoker : IUiThreadInvoker
    {
        public int BeginInvokeCallCount { get; private set; }
        public int InvokeCallCount { get; private set; }

        public T Invoke<T>(Func<T> func)
        {
            InvokeCallCount++;
            return func();
        }

        public void BeginInvoke(Action action)
        {
            BeginInvokeCallCount++;
            action();
        }
    }

}
