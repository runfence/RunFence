using Microsoft.Win32;

namespace RunFence.Launch;

public sealed class FolderHandlerRegistryValueSnapshot
{
    public required string SubKeyPath { get; init; }
    public string? ValueName { get; init; }
    public bool Existed { get; init; }
    public object? PreviousValue { get; init; }
    public RegistryValueKind? PreviousKind { get; init; }
}
