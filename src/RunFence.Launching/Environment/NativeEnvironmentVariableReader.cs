namespace RunFence.Launching.Environment;

public sealed class NativeEnvironmentVariableReader(IntPtr environmentBlock) : IEnvironmentVariableReader
{
    private Dictionary<string, string>? _variables;

    public bool TryGetValue(string name, out string? value)
    {
        _variables ??= NativeEnvironmentBlockReader.Read(environmentBlock);
        if (_variables.TryGetValue(name, out var found))
        {
            value = found;
            return true;
        }

        value = null;
        return false;
    }
}
