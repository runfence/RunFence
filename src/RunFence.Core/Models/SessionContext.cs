namespace RunFence.Core.Models;

/// <summary>
/// Bundles runtime session state. All property reads and writes must happen on the UI thread.
/// IPC handlers access this data indirectly via Form.Invoke, which serializes on the UI thread.
/// </summary>
public class SessionContext
{
    public AppDatabase Database { get; set; } = new();
    public CredentialStore CredentialStore { get; set; } = new();
    public ProtectedBuffer PinDerivedKey { get; set; } = null!;
    public Dictionary<string, string?> SidNameCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime? LastPinVerifiedAt { get; set; }
}