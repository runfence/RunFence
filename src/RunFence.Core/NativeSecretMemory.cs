using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RunFence.Core;

internal sealed class NativeSecretMemory : IDisposable
{
    private const int ErrorNotLocked = 158;

    private readonly IProtectedMemoryApi _api;
    private IntPtr _address;
    private bool _disposed;
    private bool _locked;
    private bool _protected;

    public NativeSecretMemory(int requestedLength, IProtectedMemoryApi api)
    {
        if (requestedLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedLength));

        _api = api ?? throw new ArgumentNullException(nameof(api));
        Capacity = CryptMemoryNative.RoundUpToBlockSize(requestedLength);

        bool initialized = false;
        try
        {
            _address = _api.Allocate(Capacity);
            if (_address == IntPtr.Zero)
                throw new OutOfMemoryException();

            _api.ZeroMemory(_address, Capacity);
            if (!_api.VirtualLock(_address, Capacity))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualLock failed for secret memory.");

            _locked = true;
            initialized = true;
        }
        finally
        {
            if (!initialized)
                ReleaseCore();
        }
    }

    public int Capacity { get; }

    public IntPtr Address
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _address;
        }
    }

    public bool IsProtected
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _protected;
        }
    }

    public void Protect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_protected)
            return;

        if (!_api.CryptProtectMemory(_address, Capacity))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CryptProtectMemory failed for secret memory.");

        _protected = true;
    }

    public void Unprotect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_protected)
            return;

        if (!_api.CryptUnprotectMemory(_address, Capacity))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CryptUnprotectMemory failed for secret memory.");

        _protected = false;
    }

    public void Dispose()
    {
        ReleaseCore();
        GC.SuppressFinalize(this);
    }

    ~NativeSecretMemory()
    {
        try
        {
            ReleaseCore();
        }
        catch (Exception ex)
        {
            Environment.FailFast("NativeSecretMemory finalizer cleanup failed.", ex);
        }
    }

    private void ReleaseCore()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_address == IntPtr.Zero)
            return;

        Exception? cleanupFailure = null;

        try
        {
            if (_protected && !_api.CryptUnprotectMemory(_address, Capacity))
                cleanupFailure = new Win32Exception(Marshal.GetLastWin32Error(), "CryptUnprotectMemory failed during cleanup.");
        }
        catch (Exception ex)
        {
            cleanupFailure = ex;
        }
        finally
        {
            _protected = false;
        }

        try
        {
            _api.ZeroMemory(_address, Capacity);
        }
        catch (Exception ex) when (cleanupFailure is null)
        {
            cleanupFailure = ex;
        }

        try
        {
            bool unlocked = true;
            int unlockError = 0;
            if (_locked)
            {
                unlocked = _api.VirtualUnlock(_address, Capacity);
                if (!unlocked)
                    unlockError = Marshal.GetLastWin32Error();
            }

            // ERROR_NOT_LOCKED means there is no remaining page lock to release; the buffer was
            // already zeroed above, so only other unlock failures indicate cleanup uncertainty.
            if (_locked &&
                !unlocked &&
                cleanupFailure is null &&
                unlockError != ErrorNotLocked)
            {
                cleanupFailure = new Win32Exception(unlockError, "VirtualUnlock failed during cleanup.");
            }
        }
        catch (Exception ex) when (cleanupFailure is null)
        {
            cleanupFailure = ex;
        }
        finally
        {
            _locked = false;
        }

        try
        {
            _api.Free(_address);
        }
        catch (Exception ex) when (cleanupFailure is null)
        {
            cleanupFailure = ex;
        }
        finally
        {
            _address = IntPtr.Zero;
        }

        if (cleanupFailure is not null)
            throw new InvalidOperationException("Native secret memory cleanup failed.", cleanupFailure);
    }
}
