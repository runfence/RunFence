using System.Runtime.InteropServices;

namespace RunFence.Core;

/// <summary>
/// Wraps a sensitive byte array with CryptProtectMemory and VirtualLock.
/// The caller-provided array is pinned and remains the array returned by UnprotectScope.Data.
/// NOT thread-safe: all calls must stay on the UI thread.
/// </summary>
public sealed class ProtectedBuffer : IDisposable
{
    private readonly byte[] _data;
    private readonly ProtectedMemoryProtector _protector;
    private GCHandle _handle;
    private bool _disposed;
    private bool _scopeActive;

    public ProtectedBuffer(byte[] data) : this(data, true)
    {
    }

    internal ProtectedBuffer(byte[] data, bool protect)
    {
        if (data.Length == 0 || data.Length % CryptMemoryNative.CRYPTPROTECTMEMORY_BLOCK_SIZE != 0)
            throw new ArgumentException(
                $"Buffer length must be a positive multiple of {CryptMemoryNative.CRYPTPROTECTMEMORY_BLOCK_SIZE}.",
                nameof(data));

        _data = data;
        _handle = GCHandle.Alloc(_data, GCHandleType.Pinned);
        _protector = new ProtectedMemoryProtector(NativeProtectedMemoryApi.Instance, protect);
        _protector.Lock(_handle.AddrOfPinnedObject(), _data.Length);
        _protector.Protect(_handle.AddrOfPinnedObject(), _data.Length);
    }

    public UnprotectScope Unprotect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_scopeActive)
            throw new InvalidOperationException("Buffer is already unprotected. Nested Unprotect is not supported.");

        _protector.Unprotect(_handle.AddrOfPinnedObject(), _data.Length);
        _scopeActive = true;
        return new UnprotectScope(this);
    }

    public readonly ref struct UnprotectScope(ProtectedBuffer owner)
    {
        public byte[] Data => owner._data;

        public void Dispose()
        {
            if (owner._disposed)
            {
                owner._scopeActive = false;
                return;
            }

            owner._protector.Protect(owner._handle.AddrOfPinnedObject(), owner._data.Length);
            owner._scopeActive = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _protector.ZeroBeforeRelease(_handle.AddrOfPinnedObject(), _data.Length);
        }
        finally
        {
            Array.Clear(_data);
            _handle.Free();
        }
    }
}
