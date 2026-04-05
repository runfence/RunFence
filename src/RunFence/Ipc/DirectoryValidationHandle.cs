using Microsoft.Win32.SafeHandles;

namespace RunFence.Ipc;

/// <summary>
/// Holds an open Win32 directory handle, preventing the directory from being deleted or renamed
/// while the handle is open. The canonical path is resolved before the handle is returned.
/// </summary>
public sealed class DirectoryValidationHandle : IDisposable
{
    public bool IsValid { get; init; }
    public string? CanonicalPath { get; init; }
    public string? Error { get; init; }
    private readonly SafeFileHandle? _handle;

    internal DirectoryValidationHandle(SafeFileHandle? handle)
        => _handle = handle;

    public void Dispose() => _handle?.Dispose();
}