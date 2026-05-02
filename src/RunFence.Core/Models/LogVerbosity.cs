using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogVerbosity
{
    Off = 0,
    Error = 1,
    Warning = 2,
    Info = 3,
    Debug = 4
}
