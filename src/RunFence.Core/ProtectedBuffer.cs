using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RunFence.Core;

/// <summary>
/// Wraps a sensitive byte array (e.g. pinDerivedKey) with CryptProtectMemory + VirtualLock.
/// The buffer is encrypted in-place at rest and only decrypted within an <see cref="Unprotect"/> scope.
/// NOT thread-safe — all calls must be on the UI thread.
/// </summary>
public sealed class ProtectedBuffer : IDisposable
{
    private const uint CRYPTPROTECTMEMORY_SAME_PROCESS = 0;

    private const int CRYPTPROTECTMEMORY_BLOCK_SIZE = 16;

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptProtectMemory(IntPtr pDataIn, uint cbDataIn, uint dwFlags);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectMemory(IntPtr pDataIn, uint cbDataIn, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualLock(IntPtr lpAddress, UIntPtr dwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualUnlock(IntPtr lpAddress, UIntPtr dwSize);

    private readonly byte[] _data;
    private GCHandle _handle;
    private bool _isProtected;
    private bool _disposed;
    private readonly bool _isVirtualLocked;
    private readonly bool _useProtection;
    private bool _scopeActive;

    /// <summary>
    /// Creates a ProtectedBuffer with full CryptProtectMemory + VirtualLock protection.
    /// The data array is pinned and encrypted in-place. Length must be a multiple of 16.
    /// </summary>
    public ProtectedBuffer(byte[] data) : this(data, true)
    {
    }

    /// <summary>
    /// Creates a ProtectedBuffer for testing. When protect is false, skips CryptProtectMemory/VirtualLock.
    /// </summary>
    internal ProtectedBuffer(byte[] data, bool protect)
    {
        if (data.Length == 0 || data.Length % CRYPTPROTECTMEMORY_BLOCK_SIZE != 0)
            throw new ArgumentException(
                $"Buffer length must be a positive multiple of {CRYPTPROTECTMEMORY_BLOCK_SIZE}.", nameof(data));

        _data = data;
        _useProtection = protect;
        _handle = GCHandle.Alloc(_data, GCHandleType.Pinned);

        if (protect)
        {
            _isVirtualLocked = VirtualLock(_handle.AddrOfPinnedObject(), (UIntPtr)_data.Length);
            Protect();
        }
    }

    /// <summary>
    /// Decrypts the buffer and returns an <see cref="UnprotectScope"/> that re-encrypts on dispose.
    /// Must be used with a <c>using</c> statement. Throws if already unprotected or disposed.
    /// </summary>
    public UnprotectScope Unprotect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_scopeActive)
            throw new InvalidOperationException("Buffer is already unprotected. Nested Unprotect is not supported.");

        if (_useProtection && _isProtected)
        {
            if (!CryptUnprotectMemory(_handle.AddrOfPinnedObject(), (uint)_data.Length, CRYPTPROTECTMEMORY_SAME_PROCESS))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _isProtected = false;
        }

        _scopeActive = true;
        return new UnprotectScope(this);
    }

    /// <summary>
    /// Scope that provides access to the unprotected data and re-encrypts on dispose.
    /// ref struct prevents heap storage, lambda capture, and async use.
    /// </summary>
    public readonly ref struct UnprotectScope(ProtectedBuffer owner)
    {
        /// <summary>The unprotected data. Do not store this reference beyond the using block.</summary>
        public byte[] Data => owner._data;

        public void Dispose()
        {
            if (owner._disposed)
            {
                owner._scopeActive = false;
                return;
            }

            if (owner is { _useProtection: true, _isProtected: false })
            {
                owner.Protect();
            }

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
            // Best-effort unprotect before clear. Not security-critical: the in-place
            // encryption already overwrote the plaintext, so Array.Clear zeros the buffer
            // regardless. Don't throw — Dispose must not mask exceptions during unwinding.
            if (_useProtection && _isProtected)
                CryptUnprotectMemory(_handle.AddrOfPinnedObject(), (uint)_data.Length, CRYPTPROTECTMEMORY_SAME_PROCESS);
        }
        finally
        {
            Array.Clear(_data);
            if (_isVirtualLocked)
                VirtualUnlock(_handle.AddrOfPinnedObject(), (UIntPtr)_data.Length);
            _handle.Free();
        }
    }

    private void Protect()
    {
        if (!CryptProtectMemory(_handle.AddrOfPinnedObject(), (uint)_data.Length, CRYPTPROTECTMEMORY_SAME_PROCESS))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _isProtected = true;
    }
}