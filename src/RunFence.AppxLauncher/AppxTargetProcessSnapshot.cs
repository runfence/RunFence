namespace RunFence.AppxLauncher;

public sealed class AppxTargetProcessSnapshot(IReadOnlyList<AppxTargetProcessInfo> processes)
{
    private readonly List<AppxTargetProcessInfo> _processes = [.. processes];

    public bool Contains(AppxTargetProcessInfo process)
    {
        foreach (var existingProcess in _processes)
        {
            if (existingProcess.ProcessId != process.ProcessId)
                continue;

            if (!existingProcess.StartTimeUtc.HasValue || !process.StartTimeUtc.HasValue)
                return true;

            if (existingProcess.StartTimeUtc.Value == process.StartTimeUtc.Value)
                return true;
        }

        return false;
    }
}
