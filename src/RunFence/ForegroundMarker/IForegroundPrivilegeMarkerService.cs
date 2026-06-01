namespace RunFence.ForegroundMarker;

public interface IForegroundPrivilegeMarkerService : IForegroundPrivilegeMarkerStateSource
{
    void Start(bool markerWindowEnabled, bool markerWindowEnabledWhenFullscreen);
    void SetMarkerWindowEnabled(bool enabled);
    void SetMarkerWindowEnabledWhenFullscreen(bool enabled);
    void Stop();
    void RefreshForegroundWindow();
    void OnInteractiveUserRefreshed();
}
