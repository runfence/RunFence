using System.Text.Json.Serialization;

namespace RunFence.Core.Models;

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