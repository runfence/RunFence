using Microsoft.Win32;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public interface IEphemeralAccountChangeSource
{
    event Action? AccountsChanged;
}

public interface IEphemeralContainerChangeSource
{
    event Action? ContainersChanged;
}

public interface IConfigManagementEventSource
{
    event Action? DataRefreshRequested;
    event Action? TrayUpdateRequested;
    event Action<string>? ReencryptionWarning;
}

public interface IApplicationDataChangeSource
{
    event Action? DataChanged;
}

public interface IMainFormDataRefreshTarget
{
    void SetData();
    void UpdateTray();
    void HandleDataChanged();
}

public interface IMainFormLockTarget
{
    void ShowWindowNormal();
    void ShowWindowUnlocked();
    void HandleWindowlessUnlock();
    bool ConfirmWindowsHelloUnavailableFallback();
    bool ConfirmWindowsHelloFailedFallback();
}

public interface IStartupFormLifetime
{
    event FormClosedEventHandler? FormClosed;
}

public interface IStartupIpcHost
{
    event EventHandler? HandleCreated;
    event FormClosingEventHandler? FormClosing;
    void BeginInvokeOnUiThread(Action action);
    void SetStartupComplete();
}

public interface ILockUiEventSource
{
    event Action? ShowWindowRequested;
    event Action? ShowWindowUnlockedRequested;
    event Action? WindowlessUnlockCompleted;
}

public interface IWindowsHelloPinFallbackPromptEventSource
{
    event Func<bool>? WindowsHelloUnavailableConfirmRequested;
    event Func<bool>? WindowsHelloFailedConfirmRequested;
}

public interface IWizardRequestSource
{
    event Action<IWin32Window>? WizardRequested;
    bool WizardButtonEnabled { get; set; }
}

public interface IWizardLauncher
{
    event Action? WizardCompleted;
    Task OpenWizardAsync(IWin32Window owner);
}

public interface ISessionSwitchEventSource
{
    event SessionSwitchEventHandler? SessionSwitch;
}

public interface IDragBridgeSettingsChangeSource
{
    event Action? DragBridgeSettingsChanged;
}

public interface IInputInjectionTraySink
{
    event Action? InputInjectionToggleRequested;
    void UpdateDatabase(CredentialStore credentialStore);
}
