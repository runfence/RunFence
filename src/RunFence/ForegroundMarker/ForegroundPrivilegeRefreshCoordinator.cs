using System.Drawing;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.ForegroundMarker;

public sealed class ForegroundPrivilegeRefreshCoordinator : IDisposable
{
    private readonly IForegroundWindowResolver foregroundWindowResolver;
    private readonly IProcessCreationTimeReader processCreationTimeReader;
    private readonly IForegroundWindowBoundsReader boundsReader;
    private readonly ForegroundShellWindowFilter shellWindowFilter;
    private readonly IForegroundMarkerWindow markerWindow;
    private readonly ILoggingService log;
    private long nextRequestId;
    private bool runtimeEnabled;
    private bool markerWindowEnabled;
    private bool markerWindowEnabledWhenFullscreen = true;
    private bool moveSizeActive;
    private bool disposed;
    private long enabledGeneration;
    private IntPtr trackedWindowHandle;
    private uint privilegeSubjectProcessId;
    private ForegroundPrivilegeClassificationResult? currentClassification;
    public ForegroundPrivilegeMarkerState CurrentState { get; private set; } = ForegroundPrivilegeMarkerState.Inactive;

    public ForegroundPrivilegeRefreshCoordinator(
        IForegroundWindowResolver foregroundWindowResolver,
        IProcessCreationTimeReader processCreationTimeReader,
        IForegroundWindowBoundsReader boundsReader,
        ForegroundShellWindowFilter shellWindowFilter,
        IForegroundMarkerWindow markerWindow,
        ILoggingService log)
    {
        this.foregroundWindowResolver = foregroundWindowResolver;
        this.processCreationTimeReader = processCreationTimeReader;
        this.boundsReader = boundsReader;
        this.shellWindowFilter = shellWindowFilter;
        this.markerWindow = markerWindow;
        this.log = log;
    }

    public event Action<ForegroundPrivilegeClassificationRequest>? ClassificationRequested;
    public event Action<ForegroundPrivilegeMarkerState>? StateChanged;

    public void SetRuntimeEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (runtimeEnabled == enabled)
        {
            if (!enabled)
                ClearPublishedState();
            return;
        }

        runtimeEnabled = enabled;
        enabledGeneration++;
        currentClassification = null;
        moveSizeActive = false;

        if (!enabled)
        {
            trackedWindowHandle = IntPtr.Zero;
            privilegeSubjectProcessId = 0;
            ClearPublishedState();
        }
    }

    public void SetMarkerWindowEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (markerWindowEnabled == enabled)
        {
            if (!enabled)
                HideMarker();
            return;
        }

        markerWindowEnabled = enabled;
        if (!enabled)
        {
            HideMarker();
            return;
        }

        TryRenderCurrentClassification();
    }

    public void SetMarkerWindowEnabledWhenFullscreen(bool enabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (markerWindowEnabledWhenFullscreen == enabled)
            return;

        markerWindowEnabledWhenFullscreen = enabled;
        TryRenderCurrentClassification();
    }

    public void RefreshForegroundWindow()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        RefreshForegroundWindow(foregroundWindowResolver.GetForegroundWindow());
    }

    public void RefreshForegroundWindow(IntPtr foregroundHwnd)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        RefreshForegroundWindow(foregroundWindowResolver.GetWindowInfo(foregroundHwnd));
    }

    private void RefreshForegroundWindow(ForegroundWindowInfo foregroundWindow)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!runtimeEnabled)
        {
            ClearPublishedState();
            return;
        }

        var nextTrackedWindowHandle = boundsReader.ResolveTrackedTopLevelWindow(foregroundWindow.HWnd);
        var nextPrivilegeSubjectProcessId = foregroundWindow.ProcessId;
        if (shellWindowFilter.IsShellWindow(foregroundWindow))
        {
            log.Debug(
                $"ForegroundPrivilegeRefreshCoordinator: foreground class '{foregroundWindow.ClassName}' is OS shell UI; publishing inactive state.");
            trackedWindowHandle = nextTrackedWindowHandle;
            privilegeSubjectProcessId = nextPrivilegeSubjectProcessId;
            currentClassification = null;
            moveSizeActive = false;
            ClearPublishedState();
            return;
        }

        var targetChanged = nextTrackedWindowHandle != trackedWindowHandle
                            || nextPrivilegeSubjectProcessId != privilegeSubjectProcessId;
        if (targetChanged)
        {
            moveSizeActive = false;
            ClearPublishedState();
        }

        trackedWindowHandle = nextTrackedWindowHandle;
        privilegeSubjectProcessId = nextPrivilegeSubjectProcessId;
        if (targetChanged)
            currentClassification = null;

        if (trackedWindowHandle == IntPtr.Zero || privilegeSubjectProcessId == 0)
        {
            log.Debug("ForegroundPrivilegeRefreshCoordinator: foreground target is empty; publishing inactive state.");
            currentClassification = null;
            ClearPublishedState();
            return;
        }

        if (targetChanged)
            HideMarker();

        RequestClassification(
            "foreground refresh",
            foregroundWindow.HWnd,
            trackedWindowHandle,
            privilegeSubjectProcessId);
    }

    public void RequestReclassification()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!runtimeEnabled)
        {
            ClearPublishedState();
            return;
        }

        if (trackedWindowHandle == IntPtr.Zero || privilegeSubjectProcessId == 0)
        {
            RefreshForegroundWindow();
            return;
        }

        RequestClassification(
            "reclassification",
            trackedWindowHandle,
            trackedWindowHandle,
            privilegeSubjectProcessId);
    }

    public void OnLocationChanged(IntPtr hwnd)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!runtimeEnabled || moveSizeActive || hwnd == IntPtr.Zero || hwnd != trackedWindowHandle)
            return;

        TryRenderCurrentClassification();
    }

    public void OnMoveSizeStarted(IntPtr hwnd)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!runtimeEnabled || hwnd == IntPtr.Zero || hwnd != trackedWindowHandle)
            return;

        moveSizeActive = true;
        HideMarker();
    }

    public void OnMoveSizeEnded(IntPtr hwnd)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!runtimeEnabled || hwnd == IntPtr.Zero || hwnd != trackedWindowHandle)
            return;

        moveSizeActive = false;
        TryRenderCurrentClassification();
    }

    public void ApplyClassificationResult(ForegroundPrivilegeClassificationResult result)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!runtimeEnabled
            || result.RequestId != nextRequestId
            || result.EnabledGeneration != enabledGeneration
            || result.TrackedWindowHandle != trackedWindowHandle
            || result.PrivilegeSubjectProcessId != privilegeSubjectProcessId)
        {
            log.Debug(
                $"ForegroundPrivilegeRefreshCoordinator: ignored classification request {result.RequestId}; current request={nextRequestId}, pid={privilegeSubjectProcessId}.");
            return;
        }

        if (result.IsStale || !MatchesCurrentProcessCreationTime(result))
        {
            log.Debug($"ForegroundPrivilegeRefreshCoordinator: classification request {result.RequestId} is stale; publishing inactive state.");
            currentClassification = null;
            ClearPublishedState();
            return;
        }

        currentClassification = result;
        TryRenderCurrentClassification();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        ClearPublishedState();
        markerWindow.Dispose();
        disposed = true;
    }

    private void RequestClassification(string source, IntPtr rawHwnd, IntPtr trackedHwnd, uint processId)
    {
        nextRequestId++;
        ClassificationRequested?.Invoke(
            new ForegroundPrivilegeClassificationRequest(
                nextRequestId,
                trackedHwnd,
                processId,
                enabledGeneration));
        log.Debug(
            $"ForegroundPrivilegeRefreshCoordinator: requested classification {nextRequestId} from {source}; foreground=0x{rawHwnd.ToInt64():X}, tracked=0x{trackedHwnd.ToInt64():X}, pid={processId}.");
    }

    private void TryRenderCurrentClassification()
    {
        if (!runtimeEnabled || currentClassification is null)
        {
            PublishState(ForegroundPrivilegeMarkerState.Inactive);
            HideMarker();
            return;
        }

        if (!currentClassification.IsVisible || currentClassification.Kind is not { } kind)
        {
            PublishTooltipOnlyState(currentClassification);
            HideMarker();
            return;
        }

        if (!boundsReader.TryGetVisibleBounds(trackedWindowHandle, out var bounds))
        {
            log.Debug(
                $"ForegroundPrivilegeRefreshCoordinator: visible classification {kind} has no renderable bounds for hwnd=0x{trackedWindowHandle.ToInt64():X}; publishing tooltip-only state.");
            PublishTooltipOnlyState(currentClassification);
            HideMarker();
            return;
        }

        log.Debug(
            $"ForegroundPrivilegeRefreshCoordinator: rendering visible classification {kind} for pid {privilegeSubjectProcessId} with bounds {bounds}.");
        PublishCurrentClassificationState();
        if (!markerWindowEnabled || moveSizeActive)
        {
            log.Debug(
                $"ForegroundPrivilegeRefreshCoordinator: not showing marker window; markerWindowEnabled={markerWindowEnabled}, moveSizeActive={moveSizeActive}.");
            HideMarker();
            return;
        }

        if (!markerWindowEnabledWhenFullscreen && boundsReader.IsFullscreen(trackedWindowHandle, bounds))
        {
            log.Debug("ForegroundPrivilegeRefreshCoordinator: not showing marker window for fullscreen window.");
            HideMarker();
            return;
        }

        markerWindow.Show(
            trackedWindowHandle,
            bounds,
            boundsReader.ShouldRenderInsideLeftEdge(bounds),
            ForegroundPrivilegeMarkerPalette.GetColor(kind));
    }

    private void HideMarker() => markerWindow.Hide();

    private void ClearPublishedState()
    {
        PublishState(ForegroundPrivilegeMarkerState.Inactive);
        HideMarker();
    }

    private void PublishTooltipOnlyState(ForegroundPrivilegeClassificationResult classification)
    {
        var metadata = classification.Metadata;
        if (metadata is null && classification.PrivilegeSubjectProcessId != 0)
            metadata = ForegroundPrivilegeMarkerMetadata.CreateFallback(classification.PrivilegeSubjectProcessId);

        if (metadata is null)
        {
            PublishState(ForegroundPrivilegeMarkerState.Inactive);
            return;
        }

        PublishState(ForegroundPrivilegeMarkerState.TooltipOnly(
            metadata,
            classification.IsVisible ? classification.Kind : null,
            classification.TooltipMode));
    }

    private void PublishCurrentClassificationState()
    {
        if (currentClassification is not { IsVisible: true, Kind: { } kind })
        {
            PublishState(ForegroundPrivilegeMarkerState.Inactive);
            return;
        }

        PublishState(
            ForegroundPrivilegeMarkerState.Active(
                kind,
                ForegroundPrivilegeMarkerPalette.GetColor(kind),
                currentClassification.Metadata
                ?? ForegroundPrivilegeMarkerMetadata.CreateFallback(currentClassification.PrivilegeSubjectProcessId),
                currentClassification.TooltipMode));
    }

    private void PublishState(ForegroundPrivilegeMarkerState state)
    {
        if (Equals(CurrentState, state))
            return;

        CurrentState = state;
        StateChanged?.Invoke(state);
    }

    private bool MatchesCurrentProcessCreationTime(ForegroundPrivilegeClassificationResult result)
    {
        if (!result.PrivilegeSubjectCreationTimeUtcTicks.HasValue)
            return true;

        return processCreationTimeReader.TryGetProcessCreationTimeUtcTicks(
                   result.PrivilegeSubjectProcessId,
                   out var currentCreationTimeUtcTicks)
               && currentCreationTimeUtcTicks == result.PrivilegeSubjectCreationTimeUtcTicks.Value;
    }

}
