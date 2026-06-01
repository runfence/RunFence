namespace RunFence.ForegroundMarker;

public interface IForegroundPrivilegeMarkerStateSource
{
    event Action<ForegroundPrivilegeMarkerState>? StateChanged;
    ForegroundPrivilegeMarkerState CurrentState { get; }
}
