namespace RunFence.ForegroundMarker;

public interface IForegroundPrivilegeMarkerRuntime : IDisposable
{
    event Action<ForegroundPrivilegeMarkerState>? StateChanged;
    ForegroundPrivilegeMarkerState CurrentState { get; }
    void Start(bool markerWindowEnabled, bool markerWindowEnabledWhenFullscreen);
    void Stop();
    void SetMarkerWindowEnabled(bool enabled);
    void SetMarkerWindowEnabledWhenFullscreen(bool enabled);
    void RefreshForegroundWindow();
    void RequestReclassification();
}
