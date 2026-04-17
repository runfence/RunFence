using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

/// <remarks>
/// <see cref="IsCurrentAccount"/> and <see cref="IsInteractiveUser"/> use the static
/// <see cref="SidResolutionHelper"/> because the Core layer has no DI container.
/// This makes their return values non-deterministic in unit tests.
/// </remarks>
public class CredentialEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sid { get; set; } = string.Empty;
    public byte[] EncryptedPassword { get; set; } = Array.Empty<byte>();

    [JsonIgnore]
    public bool IsCurrentAccount =>
        !string.IsNullOrEmpty(Sid) &&
        string.Equals(Sid, SidResolutionHelper.GetCurrentUserSid(), StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsInteractiveUser =>
        !IsCurrentAccount &&
        !string.IsNullOrEmpty(Sid) &&
        SidResolutionHelper.GetInteractiveUserSid() is { } sid &&
        string.Equals(Sid, sid, StringComparison.OrdinalIgnoreCase);
}