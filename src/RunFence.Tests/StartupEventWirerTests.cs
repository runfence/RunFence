using Microsoft.Win32;
using Moq;
using RunFence.Core.Models;
using RunFence.DragBridge;
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
        var ui = new InlineUiThreadInvoker(action => action());
        var target = new RecordingMainFormTarget();
        var config = new RecordingConfigManagementSource();
        var accountSource = new RecordingEphemeralAccountSource();
        var containerSource = new RecordingEphemeralContainerSource();
        var appSource = new RecordingApplicationDataSource();
        var notifier = new Mock<IDataChangeNotifier>();
        var warningPresenter = new RecordingReencryptionWarningPresenter();
        var wirer = new DataRefreshStartupEventWirer(
            ui,
            target,
            config,
            accountSource,
            containerSource,
            appSource,
            notifier.Object,
            warningPresenter);

        wirer.WireEvents();
        accountSource.RaiseAccountsChanged();
        containerSource.RaiseContainersChanged();
        config.RaiseReencryptionWarning("read-only");
        config.RaiseDataRefreshRequested();
        config.RaiseTrayUpdateRequested();
        appSource.RaiseDataChanged();

        notifier.Verify(n => n.NotifyDataChanged(), Times.Exactly(2));
        Assert.Equal("read-only", warningPresenter.LastMessage);
        Assert.Equal(1, target.SetDataCount);
        Assert.Equal(1, target.UpdateTrayCount);
        Assert.Equal(1, target.HandleDataChangedCount);
    }

    [Fact]
    public void LicenseTitle_UpdatesNotifyIconTitleOnLicenseStatusChanged()
    {
        using var notifyIcon = new NotifyIcon();
        var license = new Mock<ILicenseService>();
        license.SetupGet(l => l.IsLicensed).Returns(false);
        var wirer = new LicenseTitleStartupEventWirer(new InlineUiThreadInvoker(action => action()), license.Object, notifyIcon);

        wirer.WireEvents();
        license.Raise(l => l.LicenseStatusChanged += null);

        Assert.Contains("Evaluation", notifyIcon.Text);
    }

    [Fact]
    public void LockUi_WiresWindowRequestsAndWindowsHelloConfirmations()
    {
        var source = new RecordingLockUiEventSource();
        var fallbackSource = new RecordingWindowsHelloPinFallbackPromptEventSource();
        var target = new RecordingMainFormTarget { WindowsHelloUnavailableResult = true };
        var wirer = new LockUiStartupEventWirer(
            new InlineUiThreadInvoker(action => action()),
            source,
            fallbackSource,
            target);

        wirer.WireEvents();
        source.RaiseShowWindowRequested();
        source.RaiseShowWindowUnlockedRequested();
        source.RaiseWindowlessUnlockCompleted();
        var unavailableResult = fallbackSource.RaiseWindowsHelloUnavailableConfirmRequested();
        var failedResult = fallbackSource.RaiseWindowsHelloFailedConfirmRequested();

        Assert.Equal(1, target.ShowWindowNormalCount);
        Assert.Equal(1, target.ShowWindowUnlockedCount);
        Assert.Equal(1, target.HandleWindowlessUnlockCount);
        Assert.True(unavailableResult);
        Assert.False(failedResult);
        Assert.Equal(1, target.ConfirmWindowsHelloUnavailableCount);
        Assert.Equal(1, target.ConfirmWindowsHelloFailedCount);
    }

    [Fact]
    public void Wizard_WiresButtonRequestAndCompletionRefresh()
    {
        var source = new RecordingWizardRequestSource();
        var launcher = new RecordingWizardLauncher();
        var target = new RecordingMainFormTarget();
        var wirer = new WizardStartupEventWirer(source, launcher, target);

        wirer.WireEvents();
        source.RaiseWizardRequested(new Mock<IWin32Window>().Object);
        launcher.RaiseWizardCompleted();

        Assert.True(source.WizardButtonEnabled);
        Assert.Equal(1, launcher.OpenWizardCount);
        Assert.Equal(1, target.HandleDataChangedCount);
    }

    [Fact]
    public void SessionSwitch_InvalidatesInteractiveDesktopCacheForHandledReasonsAndUnsubscribesOnClose()
    {
        var source = new RecordingSessionSwitchEventSource();
        var lifetime = new RecordingFormLifetime();
        var desktopProvider = new Mock<IInteractiveUserDesktopProvider>();
        var wirer = new SessionSwitchStartupEventWirer(source, lifetime, desktopProvider.Object);

        wirer.WireEvents();
        source.Raise(SessionSwitchReason.SessionLock);
        source.Raise(SessionSwitchReason.ConsoleConnect);
        lifetime.RaiseFormClosed();
        source.Raise(SessionSwitchReason.ConsoleConnect);

        desktopProvider.Verify(d => d.InvalidateCache(), Times.Once);
    }

    [Fact]
    public void DragBridge_WiresDataSettingsAndDispose()
    {
        var session = new SessionContext { Database = new AppDatabase(), CredentialStore = new CredentialStore() };
        var appSource = new RecordingApplicationDataSource();
        var lifetime = new RecordingFormLifetime();
        var settingsSource = new RecordingDragBridgeSettingsChangeSource();
        var dragBridge = new Mock<IDragBridgeService>();
        var wirer = new DragBridgeEventWirer(
            new InlineUiThreadInvoker(action => action()),
            lifetime,
            appSource,
            new LambdaSessionProvider(() => session),
            settingsSource,
            dragBridge.Object);

        wirer.WireEvents();
        appSource.RaiseDataChanged();
        settingsSource.RaiseDragBridgeSettingsChanged();
        lifetime.RaiseFormClosed();

        dragBridge.Verify(d => d.SetData(session), Times.Once);
        dragBridge.Verify(d => d.ApplySettings(session.Database.Settings), Times.Once);
        dragBridge.Verify(d => d.Dispose(), Times.Once);
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
        public Task OpenWizardAsync(IWin32Window owner)
        {
            OpenWizardCount++;
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

    private sealed class RecordingDragBridgeSettingsChangeSource : IDragBridgeSettingsChangeSource
    {
        public event Action? DragBridgeSettingsChanged;
        public void RaiseDragBridgeSettingsChanged() => DragBridgeSettingsChanged?.Invoke();
    }

}
