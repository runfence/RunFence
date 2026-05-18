using RunFence.Core.Models;

namespace RunFence.RunAs;

public sealed record RunAsCreatedAccountPostSetupRequest
{
    public string? CreatedSid { get; init; }
    public string? Username { get; init; }
    public required string FilePath { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public bool IsEphemeral { get; init; }
    public PrivilegeLevel SelectedPrivilegeLevel { get; init; }
    public bool FirewallSettingsChanged { get; init; }
    public bool AllowInternet { get; init; }
    public bool AllowLocalhost { get; init; }
    public bool AllowLan { get; init; }
    public string? SettingsImportPath { get; init; }
}
