using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RunFence.Security;

/// <summary>
/// Disposable struct for passing a PIN-derived key across a secure desktop boundary.
/// Pins the byte array in memory to prevent GC relocation, and zeros it on dispose.
/// Always call <see cref="ExtractPinDerivedKey"/> on the UI thread before
/// <see cref="ISecureDesktopRunner.Run"/> — do not use inside async methods (ref struct constraint).
/// </summary>
public readonly struct PinnedKeyBuffer(byte[] data) : IDisposable
{
    public byte[] Data { get; } = data;
    private readonly GCHandle _handle = GCHandle.Alloc(data, GCHandleType.Pinned);

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(Data);
        if (_handle.IsAllocated)
            _handle.Free();
    }
}