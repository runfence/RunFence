using RunFence.Launch.Tokens;

namespace RunFence.Launch;

public sealed class LaunchExecutionResult : IDisposable
{
    private ProcessInfo? _process;

    public LaunchExecutionResult(
        LaunchExecutionStatus status,
        ProcessInfo? process,
        IReadOnlyList<string>? maintenanceWarnings = null)
    {
        Status = status;
        _process = process;
        MaintenanceWarnings = maintenanceWarnings?.ToArray() ?? [];
    }

    public LaunchExecutionStatus Status { get; }
    public ProcessInfo? Process => _process;
    public IReadOnlyList<string> MaintenanceWarnings { get; }

    public ProcessInfo? DetachProcess()
    {
        var process = _process;
        _process = null;
        return process;
    }

    public void Dispose()
    {
        var process = _process;
        _process = null;
        process?.Dispose();
    }
}
