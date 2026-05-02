using RunFence.Core;

namespace RunFence.Launch;

public sealed record LaunchTargetResolutionResult : IDisposable
{
    private IDisposable? _hiveLease;
    private readonly ILoggingService? _log;

    public LaunchTargetResolutionResult(
        ProcessLaunchTarget target,
        LaunchResolutionKind kind,
        IDisposable? hiveLease,
        ILoggingService? log = null)
    {
        Target = target;
        Kind = kind;
        _hiveLease = hiveLease;
        _log = log;
    }

    public ProcessLaunchTarget Target { get; }
    public LaunchResolutionKind Kind { get; }

    public void Dispose()
    {
        var hiveLease = Interlocked.Exchange(ref _hiveLease, null);
        if (hiveLease == null)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                hiveLease.Dispose();
            }
            catch (Exception ex)
            {
                _log?.Error("LaunchTargetResolutionResult: asynchronous hive lease disposal failed.", ex);
            }
        });
    }
}
