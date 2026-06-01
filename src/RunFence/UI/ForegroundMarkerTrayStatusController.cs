using RunFence.Account;
using RunFence.ForegroundMarker;
using RunFence.TrayIcon;

namespace RunFence.UI;

public sealed class ForegroundMarkerTrayStatusController(
    NotifyIcon notifyIcon,
    ITrayForegroundMarkerOverlaySink trayForegroundMarkerOverlaySink,
    IForegroundPrivilegeMarkerStateSource markerStateSource,
    ISidNameCacheService sidNameCacheService,
    ApplicationCaptionTextBuilder captionTextBuilder)
    : IDisposable
{
    private readonly Dictionary<string, string> _sidDisplayNameCache = new(StringComparer.OrdinalIgnoreCase);
    private IMainFormVisibility? _form;
    private ForegroundPrivilegeMarkerState _currentState = ForegroundPrivilegeMarkerState.Inactive;
    private bool _initialized;
    private bool _disposed;

    public void Initialize(IMainFormVisibility form)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
            return;

        _form = form;
        markerStateSource.StateChanged += OnMarkerStateChanged;
        _initialized = true;
        ApplyState(markerStateSource.CurrentState, requireHandle: false);
    }

    public void UpdateTrayTooltip()
    {
        if (_disposed || !_initialized)
            return;

        ApplyState(_currentState, requireHandle: false);
    }

    public void UpdateTray()
    {
        if (_disposed || !_initialized)
            return;

        _sidDisplayNameCache.Clear();
        ApplyState(_currentState, requireHandle: false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_initialized)
            markerStateSource.StateChanged -= OnMarkerStateChanged;
    }

    private void OnMarkerStateChanged(ForegroundPrivilegeMarkerState state)
    {
        if (_disposed)
            return;

        var form = _form;
        if (form == null || form.IsDisposed || !form.IsHandleCreated)
            return;

        try
        {
            form.BeginInvokeOnUiThread(() => ApplyState(state, requireHandle: true));
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ApplyState(ForegroundPrivilegeMarkerState state, bool requireHandle)
    {
        if (_disposed)
            return;

        var form = _form;
        if (form == null || form.IsDisposed || (requireHandle && !form.IsHandleCreated))
            return;

        _currentState = state;

        if (state.Metadata is null)
        {
            notifyIcon.Text = captionTextBuilder.BuildBaseTrayTooltip();
            trayForegroundMarkerOverlaySink.SetForegroundMarkerOverlay(null);
            return;
        }

        var metadata = state.Metadata;
        string accountName;
        if (string.IsNullOrWhiteSpace(metadata.AccountSid))
        {
            accountName = "Unknown account";
        }
        else if (_sidDisplayNameCache.TryGetValue(metadata.AccountSid, out var cachedDisplayName))
        {
            accountName = cachedDisplayName;
        }
        else
        {
            accountName = sidNameCacheService.GetDisplayName(metadata.AccountSid);
            _sidDisplayNameCache[metadata.AccountSid] = accountName;
        }

        var modeLabel = state.TooltipMode switch
        {
            ForegroundPrivilegeTooltipMode.Isolated => "[Isolated]",
            ForegroundPrivilegeTooltipMode.LowIL => "[LowIL]",
            ForegroundPrivilegeTooltipMode.HighIL => "[HighIL]",
            ForegroundPrivilegeTooltipMode.Elevated => "[Elevated]",
            _ => null,
        };

        notifyIcon.Text = captionTextBuilder.BuildForegroundMarkerTrayTooltip(
            metadata.ProcessName,
            accountName,
            modeLabel);
        trayForegroundMarkerOverlaySink.SetForegroundMarkerOverlay(state.IsActive && state.Color is { } color ? color : null);
    }
}
