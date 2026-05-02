using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

public sealed record JobKeeperInstanceIdentity
{
    public required string TargetSid { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required JobKeeperIntegrityMode ExpectedMode { get; init; }

    public required string InstanceId { get; init; }
    public required string PipeName { get; init; }
    public required string JobName { get; init; }
    public int LastVerifiedKeeperPid { get; set; }

    public static string CreateKey(string targetSid, bool isLow) =>
        $"{targetSid}|{(isLow ? JobKeeperIntegrityMode.LowIntegrity : JobKeeperIntegrityMode.Restricted)}";

    public static JobKeeperIntegrityMode GetMode(bool isLow) =>
        isLow ? JobKeeperIntegrityMode.LowIntegrity : JobKeeperIntegrityMode.Restricted;
}
