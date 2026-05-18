namespace RunFence.Core;

internal sealed class ProtectedStringNativeBuffer : IDisposable
{
    private readonly IProtectedMemoryApi _protectedMemoryApi;
    private readonly bool _useProtection;
    private NativeSecretMemory? _memory;

    internal ProtectedStringNativeBuffer(IProtectedMemoryApi protectedMemoryApi, bool useProtection)
    {
        _protectedMemoryApi = protectedMemoryApi ?? throw new ArgumentNullException(nameof(protectedMemoryApi));
        _useProtection = useProtection;
        _memory = CreateMemory(0);
    }

    public int Capacity => GetRequiredMemory().Capacity;

    public bool IsDisposed => _memory == null;

    public void EnsureCapacity(int requiredByteLength)
    {
        if (requiredByteLength < 0)
            throw new ArgumentOutOfRangeException(nameof(requiredByteLength));

        NativeSecretMemory current = GetRequiredMemory();
        if (requiredByteLength <= current.Capacity)
            return;

        NativeSecretMemory? replacement = null;
        bool reprotectCurrent = false;
        try
        {
            replacement = new NativeSecretMemory(Math.Max(1, requiredByteLength), _protectedMemoryApi);
            reprotectCurrent = UnprotectForAccess(current);
            if (current.Capacity > 0)
                _protectedMemoryApi.CopyMemory(current.Address, replacement.Address, current.Capacity);

            if (_useProtection)
                replacement.Protect();

            _memory = replacement;
            replacement = null;
            try
            {
                current.Dispose();
            }
            catch (Exception ex)
            {
                Environment.FailFast(
                    "ProtectedString old native buffer cleanup failed after replacement.",
                    ex);
            }
        }
        finally
        {
            Exception? replacementCleanupFailure = null;
            try
            {
                replacement?.Dispose();
            }
            catch (Exception ex)
            {
                replacementCleanupFailure = ex;
            }

            try
            {
                if (reprotectCurrent && _memory == current)
                    ReprotectAfterAccess(current, reprotect: true);
            }
            catch (Exception ex) when (replacementCleanupFailure != null)
            {
                Environment.FailFast(
                    "ProtectedString native buffer cleanup and re-protection failed.",
                    new AggregateException(replacementCleanupFailure, ex));
            }

            if (replacementCleanupFailure != null)
            {
                Environment.FailFast(
                    "ProtectedString replacement buffer cleanup failed.",
                    replacementCleanupFailure);
            }
        }
    }

    public void Clear() =>
        WithUnprotectedAccess(access => _protectedMemoryApi.ZeroMemory(access.Address, access.Capacity));

    internal void ReplaceContent(ReadOnlySpan<byte> utf16Bytes)
    {
        EnsureCapacity(utf16Bytes.Length);
        NativeSecretMemory memory = GetRequiredMemory();
        bool reprotect = UnprotectForAccess(memory);
        try
        {
            _protectedMemoryApi.ZeroMemory(memory.Address, memory.Capacity);
            CopyUtf16BytesToNative(utf16Bytes, memory.Address);
        }
        finally
        {
            ReprotectAfterAccess(memory, reprotect);
        }
    }

    internal void CopyTo(ProtectedStringNativeBuffer target, int byteLength)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (byteLength < 0)
            throw new ArgumentOutOfRangeException(nameof(byteLength));

        NativeSecretMemory sourceMemory = GetRequiredMemory();
        NativeSecretMemory targetMemory = target.GetRequiredMemory();
        bool reprotectSource = UnprotectForAccess(sourceMemory);
        bool reprotectTarget = target.UnprotectForAccess(targetMemory);
        try
        {
            target._protectedMemoryApi.ZeroMemory(targetMemory.Address, targetMemory.Capacity);
            if (byteLength > 0)
                _protectedMemoryApi.CopyMemory(sourceMemory.Address, targetMemory.Address, byteLength);
        }
        finally
        {
            Exception? reprotectFailure = null;
            try
            {
                target.ReprotectAfterAccess(targetMemory, reprotectTarget);
            }
            catch (Exception ex)
            {
                reprotectFailure = ex;
            }

            try
            {
                ReprotectAfterAccess(sourceMemory, reprotectSource);
            }
            catch (Exception ex)
            {
                reprotectFailure = reprotectFailure == null
                    ? ex
                    : new AggregateException(reprotectFailure, ex);
            }

            if (reprotectFailure != null)
                throw reprotectFailure;
        }
    }

    public void WithUnprotectedAccess(ProtectedStringBufferAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        WithUnprotectedAccess(
            access =>
            {
                action(access);
                return default(VoidStruct);
            });
    }

    public T WithUnprotectedAccess<T>(ProtectedStringBufferFunc<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        NativeSecretMemory memory = GetRequiredMemory();
        bool reprotect = UnprotectForAccess(memory);
        try
        {
            return action(new ProtectedStringBufferAccess(memory.Address, memory.Capacity));
        }
        finally
        {
            ReprotectAfterAccess(memory, reprotect);
        }
    }

    public void Dispose()
    {
        NativeSecretMemory? memoryToDispose = Interlocked.Exchange(ref _memory, null);
        memoryToDispose?.Dispose();
    }

    private NativeSecretMemory CreateMemory(int logicalByteLength)
    {
        var memory = new NativeSecretMemory(Math.Max(1, logicalByteLength), _protectedMemoryApi);
        if (_useProtection)
            memory.Protect();

        return memory;
    }

    private NativeSecretMemory GetRequiredMemory() =>
        _memory ?? throw new ObjectDisposedException(nameof(ProtectedStringNativeBuffer));

    private bool UnprotectForAccess(NativeSecretMemory memory)
    {
        if (!_useProtection || !memory.IsProtected)
            return false;

        memory.Unprotect();
        return true;
    }

    private void ReprotectAfterAccess(NativeSecretMemory memory, bool reprotect)
    {
        if (!_useProtection || !reprotect)
            return;

        try
        {
            memory.Protect();
        }
        catch (Exception ex)
        {
            DisposeAfterReprotectFailure(memory, ex);
        }
    }

    private void DisposeAfterReprotectFailure(NativeSecretMemory memory, Exception protectFailure)
    {
        if (ReferenceEquals(_memory, memory))
            _memory = null;

        try
        {
            memory.Dispose();
        }
        catch (Exception cleanupException)
        {
            Environment.FailFast(
                "ProtectedString native buffer cleanup failed after re-protection failure.",
                cleanupException);
        }

        throw new InvalidOperationException(
            "ProtectedString native buffer re-protection failed. The buffer has been disposed.",
            protectFailure);
    }

    private static unsafe void CopyUtf16BytesToNative(ReadOnlySpan<byte> source, IntPtr destination)
    {
        if (source.IsEmpty)
            return;

        fixed (byte* sourcePtr = source)
        {
            Buffer.MemoryCopy(sourcePtr, destination.ToPointer(), source.Length, source.Length);
        }
    }
}
