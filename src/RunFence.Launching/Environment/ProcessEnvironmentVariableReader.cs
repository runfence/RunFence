using System.Collections;

namespace RunFence.Launching.Environment;

public sealed class ProcessEnvironmentVariableReader : IEnvironmentVariableReader
{
    public bool TryGetValue(string name, out string? value)
    {
        value = System.Environment.GetEnvironmentVariable(name);
        return value != null;
    }

    public static Dictionary<string, string> ReadAll()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
                result[key] = entry.Value as string ?? string.Empty;
        }

        return result;
    }
}
