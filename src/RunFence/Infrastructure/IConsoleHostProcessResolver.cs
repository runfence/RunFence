namespace RunFence.Infrastructure;

public interface IConsoleHostProcessResolver
{
    bool TryGetConsoleHostProcessId(int processId, out int consoleHostProcessId);
}
