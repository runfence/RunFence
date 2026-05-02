using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace RunFence.Core;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options;

    static JsonDefaults()
    {
        Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        Options.MakeReadOnly();
    }
}
