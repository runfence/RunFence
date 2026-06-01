using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Licensing.UI.Forms;
using RunFence.Persistence.UI;
using RunFence.Startup.UI;

namespace RunFence.UI;

/// <summary>
/// Handles show-window and elevated-unlock IPC requests on behalf of the main form.
/// Implements <see cref="IElevatedUnlockRequestHandler"/> and <see cref="IShowWindowRequestHandler"/>
/// so IPC callers and the tray handler can share a single resolved entry point.
/// </summary>
public sealed class MainFormWindowRequestHandler(
    LockManager lockManager,
    IConfigAvailabilityChecker configAvailabilityChecker,
    IShellHelper shellHelper,
    ILicenseService licenseService,
    ILoggingService log)
    : IElevatedUnlockRequestHandler, IOperationUnlockRequestHandler, IShowWindowRequestHandler
{
    private IMainFormVisibility _form = null!;
    private bool _startupComplete;
    private readonly TaskCompletionSource _startupCompleteSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Raised after the nag dialog closes and the license was activated, so the tray handler
    /// can update the title/tooltip immediately without waiting for the async license event.
    /// </summary>
    public event Action? TitleUpdateNeeded;

    public bool IsShowingWindow { get; private set; }
    public bool IsStartupComplete => _startupComplete;

    public void Initialize(IMainFormVisibility form)
    {
        if (_form != null)
            throw new InvalidOperationException("MainFormWindowRequestHandler.Initialize can only be called once.");

        _form = form;
    }

    public void SetStartupComplete()
    {
        _startupComplete = true;
        _startupCompleteSource.TrySetResult();
    }

    public void CancelStartup()
    {
        _startupCompleteSource.TrySetCanceled();
    }

    public void ShowAndActivate()
    {
        var form = GetForm();
        form.ShowInTaskbar = true;
        IsShowingWindow = true;
        try
        {
            // Reset to Normal before Show so the form never appears in minimized state.
            form.WindowState = FormWindowState.Normal;
            form.Show();
        }
        finally
        {
            IsShowingWindow = false;
        }

        ForceForeground();
    }

    public void ShowAndActivateForUnlock()
    {
        // Show before setting WindowState — prevents brief minimized flash when unlocking.
        // The IsShowingWindow guard prevents HandleResize from immediately hiding the form again
        // while it is transitioning out of the startup-minimized state.
        var form = GetForm();
        form.ShowInTaskbar = true;
        IsShowingWindow = true;
        try
        {
            form.Show();
            form.WindowState = FormWindowState.Normal;
        }
        finally
        {
            IsShowingWindow = false;
        }

        ForceForeground();
    }

    public async Task TryShowWindowAsync()
    {
        if (!_startupComplete)
            return;
        var wasLocked = lockManager.IsLocked;
        await lockManager.TryShowWindowAsync();
        if (wasLocked && !lockManager.IsLocked)
            configAvailabilityChecker.ScheduleAvailabilityCheck();
        if (!lockManager.IsLocked)
        {
            ShowNagIfNeeded();
            ForceForeground();
        }
    }

    public void RequestShowWindow()
    {
        _ = ExecutePostedRequestAsync(
            requestName: "show window",
            action: TryShowWindowWithLoggingAsync);
    }

    public void RequestOperationUnlock()
    {
        _ = ExecutePostedRequestAsync(
            requestName: "operation unlock",
            action: TryOperationUnlockWithLoggingAsync);
    }

    public async Task<bool> HandleElevatedUnlockRequestAsync()
    {
        if (!_startupComplete)
        {
            try
            {
                await _startupCompleteSource.Task;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

        if (!lockManager.IsLocked)
        {
            await TryShowWindowAsync();
            return true;
        }

        var wasLocked = lockManager.IsLocked;
        var unlocked = await lockManager.TryUnlockAsync(isAdmin: true);
        if (wasLocked && !lockManager.IsLocked)
            configAvailabilityChecker.ScheduleAvailabilityCheck();
        if (!lockManager.IsLocked)
        {
            ShowNagIfNeeded();
            ForceForeground();
        }

        return unlocked;
    }

    public async Task<bool> HandleOperationUnlockRequestAsync()
    {
        if (!_startupComplete)
        {
            try
            {
                await _startupCompleteSource.Task;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

        var wasLocked = lockManager.IsLocked;
        var completed = !lockManager.IsLocked || lockManager.CompletePendingOperationUnlock();
        if (wasLocked && !lockManager.IsLocked)
            configAvailabilityChecker.ScheduleAvailabilityCheck();
        return completed;
    }

    private async Task TryOperationUnlockWithLoggingAsync()
    {
        try
        {
            await lockManager.TryUnlockForOperationAsync(isAdmin: false);
        }
        catch (Exception ex)
        {
            log.Error("Failed to handle posted operation unlock request", ex);
        }
    }

    public void ShowNagIfNeeded()
    {
        if (!licenseService.ShouldShowNag(DateTime.Now))
            return;
        var form = GetForm();
        using var dlg = new EvaluationNagDialog(licenseService, shellHelper);
        dlg.ShowDialog((Control)form);
        if (licenseService.IsLicensed)
            TitleUpdateNeeded?.Invoke();
        else
            licenseService.RecordNagShown(DateTime.Now);
    }

    private async Task TryShowWindowWithLoggingAsync()
    {
        try
        {
            await TryShowWindowAsync();
        }
        catch (Exception ex)
        {
            log.Error("Failed to handle posted show window request", ex);
        }
    }

    private async Task<bool> WaitForStartupCompleteAsync()
    {
        if (_startupComplete)
            return true;

        try
        {
            await _startupCompleteSource.Task.ConfigureAwait(false);
            return true;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private async Task ExecutePostedRequestAsync(string requestName, Func<Task> action)
    {
        try
        {
            if (!await WaitForStartupCompleteAsync())
                return;

            GetForm().BeginInvokeOnUiThread(() => _ = action());
        }
        catch (Exception ex)
        {
            log.Error($"Failed to queue posted {requestName} request", ex);
        }
    }

    private void ForceForeground()
    {
        var form = GetForm();
        WindowForegroundHelper.ForceToForeground(form.Handle);
        form.BringToFront();
    }

    private IMainFormVisibility GetForm()
        => _form ?? throw new InvalidOperationException("MainFormWindowRequestHandler must be initialized before use.");
}
