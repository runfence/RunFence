namespace RunFence.Launching.Environment;

public sealed class DictionaryEnvironmentVariableReader(IReadOnlyDictionary<string, string> variables)
    : IEnvironmentVariableReader
{
    public bool TryGetValue(string name, out string? value)
    {
        if (variables.TryGetValue(name, out var found))
        {
            value = found;
            return true;
        }

        value = null;
        return false;
    }
}
