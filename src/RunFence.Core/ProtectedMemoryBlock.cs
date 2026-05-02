namespace RunFence.Core;

public sealed class ProtectedMemoryBlock : IDisposable
{
    private readonly IProtectedMemoryApi _api;
    private readonly bool _useProtection;
    private ProtectedMemoryProtector _protector;
    private IntPtr _address;
    private int _capacity;
    private bool _disposed;
    private bool _scopeActive;

    public ProtectedMemoryBlock(int byteCapacity)
        : this(byteCapacity, protect: true, NativeProtectedMemoryApi.Instance)
    {
    }

    internal ProtectedMemoryBlock(int byteCapacity, bool protect, IProtectedMemoryApi api)
    {
        if (byteCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteCapacity));

        _api = api;
        _useProtection = protect;
        _capacity = RoundUpToBlockSize(byteCapacity);
        _protector = new ProtectedMemoryProtector(_api, _useProtection);
        _address = _api.Allocate(_capacity);
        _api.ZeroMemory(_address, _capacity);
        _protector.Lock(_address, _capacity);
        _protector.Protect(_address, _capacity);
    }

    public int Capacity
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _capacity;
        }
    }

    public bool IsProtected
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _protector.IsProtected;
        }
    }

    internal IntPtr Address
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _address;
        }
    }

    public UnprotectScope Unprotect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_scopeActive)
            throw new InvalidOperationException("Block is already unprotected. Nested Unprotect is not supported.");

        _protector.Unprotect(_address, _capacity);
        _scopeActive = true;
        return new UnprotectScope(this);
    }

    public void EnsureCapacity(int requiredByteCapacity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (requiredByteCapacity <= _capacity)
            return;

        if (_scopeActive)
            throw new InvalidOperationException("Cannot resize while unprotected.");

        bool wasProtected = _protector.IsProtected;
        _protector.Unprotect(_address, _capacity);

        int newCapacity = RoundUpToBlockSize(requiredByteCapacity);
        IntPtr newAddress = IntPtr.Zero;
        var newProtector = new ProtectedMemoryProtector(_api, _useProtection);
        bool swapped = false;

        try
        {
            newAddress = _api.Allocate(newCapacity);
            _api.ZeroMemory(newAddress, newCapacity);
            newProtector.Lock(newAddress, newCapacity);
            _api.CopyMemory(_address, newAddress, _capacity);
            if (wasProtected)
                newProtector.Protect(newAddress, newCapacity);

            _protector.ZeroBeforeRelease(_address, _capacity);
            _api.Free(_address);

            _address = newAddress;
            _capacity = newCapacity;
            _protector = newProtector;
            swapped = true;
            newAddress = IntPtr.Zero;
        }
        catch
        {
            if (!swapped && wasProtected && _address != IntPtr.Zero)
                _protector.Protect(_address, _capacity);

            throw;
        }
        finally
        {
            if (newAddress != IntPtr.Zero)
            {
                newProtector.ZeroBeforeRelease(newAddress, newCapacity);
                _api.Free(newAddress);
            }
        }
    }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    ~ProtectedMemoryBlock()
    {
        try
        {
            Release();
        }
        catch
        {
        }
    }

    private void Release()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_address == IntPtr.Zero)
            return;

        _protector.ZeroBeforeRelease(_address, _capacity);
        _api.Free(_address);
        _address = IntPtr.Zero;
        _scopeActive = false;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var scope = Unprotect();
        _api.ZeroMemory(scope.Address, _capacity);
    }

    public readonly ref struct UnprotectScope
    {
        private readonly ProtectedMemoryBlock? _owner;

        internal UnprotectScope(ProtectedMemoryBlock owner)
        {
            _owner = owner;
        }

        public IntPtr Address => _owner?._address
            ?? throw new InvalidOperationException("UnprotectScope is not initialized.");

        public void Dispose()
        {
            if (_owner == null)
                return;

            if (_owner._disposed)
            {
                _owner._scopeActive = false;
                return;
            }

            _owner._protector.Protect(_owner._address, _owner._capacity);
            _owner._scopeActive = false;
        }
    }

    internal static int RoundUpToBlockSize(int value) =>
        (value + CryptMemoryNative.CRYPTPROTECTMEMORY_BLOCK_SIZE - 1) &
        ~(CryptMemoryNative.CRYPTPROTECTMEMORY_BLOCK_SIZE - 1);
}
