namespace RunFence.ForegroundMarker;

public interface IForegroundMarkerThreadDispatcher : IDisposable
{
    void Start();
    void Stop();
    void Post(Action action);
    void Invoke(Action action);
    bool IsCurrentThread { get; }
}
