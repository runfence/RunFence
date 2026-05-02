namespace RunFence.Launching.Environment;

public interface IEnvironmentVariableReader
{
    bool TryGetValue(string name, out string? value);
}
