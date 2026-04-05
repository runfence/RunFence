namespace RunFence.Infrastructure;

/// <summary>
/// Adapts delegates to the <see cref="IUiThreadInvoker"/> interface.
/// Used in composition roots where the UI thread marshal target is captured via closure.
/// </summary>
public class LambdaUiThreadInvoker(
    Action<Action> invoke,
    Action<Action>? beginInvoke = null,
    Action<Action>? runOnUiThread = null) : IUiThreadInvoker
{
    public void Invoke(Action action) => invoke(action);
    public void BeginInvoke(Action action) => (beginInvoke ?? invoke)(action);
    public void RunOnUiThread(Action action) => (runOnUiThread ?? invoke)(action);
}