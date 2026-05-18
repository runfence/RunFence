using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

public sealed record AppEnforcementRetryStatus(
    string FailureMessage,
    DateTime LastFailedUtc)
{
    [JsonIgnore]
    public bool IsRetryPending => true;
}
