using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RunFence.Core;

namespace RunFence.Security;

/// <summary>
/// Disposable class for passing a PIN-derived key across a secure desktop boundary.
/// Pins the byte array in memory to prevent GC relocation, and zeros it on dispose.
/// Always call <see cref="FromProtected"/> on the UI thread before <see cref="ISecureDesktopRunner.Run"/>.
/// </summary>
public class PinnedKeyBuffer(byte[] data) : IDisposable
{
    public byte[] Data { get; } = data;
    private GCHandle _handle = GCHandle.Alloc(data, GCHandleType.Pinned);

    public static PinnedKeyBuffer FromProtected(ProtectedBuffer pinDerivedKey)
    {
        using var scope = pinDerivedKey.Unprotect();
        return new PinnedKeyBuffer(scope.Data.ToArray());
    }

    ~PinnedKeyBuffer() => Dispose();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        CryptographicOperations.ZeroMemory(Data);
        if (_handle.IsAllocated)
            _handle.Free();
    }
}