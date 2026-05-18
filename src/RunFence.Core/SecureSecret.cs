using System.Collections.Generic;

namespace RunFence.Core;

public sealed class SecureSecret : ISecureSecretSnapshotSource, IDisposable
{
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(1);
    private static readonly HashSet<Type> NoAdditionalRejectedReturnTypes = [];

    private readonly object _gate = new();
    private readonly object _callbackThreadsGate = new();
    private readonly Dictionary<int, int> _activeCallbackThreads = [];
    private readonly IProtectedMemoryApi _api;
    private readonly TimeSpan _lockTimeout;
    private NativeSecretMemory? _master;
    private readonly int _realLength;
    private bool _disposed;

    public SecureSecret(int realLength, SecureSecretInitializer initialize)
        : this(realLength, initialize, NativeProtectedMemoryApi.Instance, null)
    {
    }

    internal SecureSecret(
        int realLength,
        SecureSecretInitializer initialize,
        IProtectedMemoryApi api,
        TimeSpan? lockTimeout)
    {
        if (realLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(realLength));

        _realLength = realLength;
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _lockTimeout = lockTimeout ?? DefaultLockTimeout;

        ArgumentNullException.ThrowIfNull(initialize);
        ValidateTimeout(_lockTimeout, nameof(lockTimeout));

        NativeSecretMemory? master = null;
        try
        {
            master = new NativeSecretMemory(realLength, _api);
            unsafe
            {
                initialize(new Span<byte>(master.Address.ToPointer(), realLength));
            }

            master.Protect();
            _master = master;
        }
        catch
        {
            master?.Dispose();
            throw;
        }
    }

    public void UseSnapshot(SecureSecretAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        TransformSnapshot(data =>
        {
            action(data);
            return new VoidStruct();
        });
    }

    public T TransformSnapshot<T>(SecureSecretFunc<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        SecretSnapshotCallbackValidator.RejectUnsupportedReturnType<T>(
            "SecureSecret callbacks",
            NoAdditionalRejectedReturnTypes);

        NativeSecretMemory? snapshot = null;
        bool lockTaken = false;

        if (!Monitor.TryEnter(_gate, _lockTimeout))
            throw new TimeoutException("Timed out waiting to create a SecureSecret snapshot.");

        lockTaken = true;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            NativeSecretMemory master = _master
                ?? throw new ObjectDisposedException(nameof(SecureSecret));

            master.Unprotect();
            unsafe
            {
                try
                {
                    try
                    {
                        snapshot = new NativeSecretMemory(_realLength, _api);
                        _api.CopyMemory(master.Address, snapshot.Address, _realLength);
                    }
                    catch
                    {
                        snapshot?.Dispose();
                        throw;
                    }
                }
                finally
                {
                    try
                    {
                        master.Protect();
                    }
                    catch (Exception ex)
                    {
                        HandleMasterReprotectFailure(master, snapshot, ex);
                    }
                }

                int currentThreadId = Environment.CurrentManagedThreadId;
                lock (_callbackThreadsGate)
                {
                    _activeCallbackThreads.TryGetValue(currentThreadId, out int depth);
                    _activeCallbackThreads[currentThreadId] = depth + 1;
                }

                try
                {
                    Monitor.Exit(_gate);
                    lockTaken = false;
                    return action(new ReadOnlySpan<byte>(snapshot.Address.ToPointer(), _realLength));
                }
                finally
                {
                    try
                    {
                        snapshot.Dispose();
                    }
                    finally
                    {
                        lock (_callbackThreadsGate)
                        {
                            if (_activeCallbackThreads.TryGetValue(currentThreadId, out int depth))
                            {
                                if (depth == 1)
                                    _activeCallbackThreads.Remove(currentThreadId);
                                else
                                    _activeCallbackThreads[currentThreadId] = depth - 1;
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            if (lockTaken)
                Monitor.Exit(_gate);
        }
    }

    public bool TryDispose(TimeSpan timeout)
    {
        ThrowIfDisposingFromActiveCallback();
        ValidateTimeout(timeout, nameof(timeout));

        bool disposed = TryDisposeCore(timeout, out Exception? cleanupFailure);
        if (cleanupFailure is not null)
            throw cleanupFailure;

        if (disposed)
            GC.SuppressFinalize(this);

        return disposed;
    }

    public void DisposeOrThrow(TimeSpan timeout)
    {
        ThrowIfDisposingFromActiveCallback();
        ValidateTimeout(timeout, nameof(timeout));

        if (!TryDisposeCore(timeout, out Exception? cleanupFailure))
            throw new TimeoutException("Timed out waiting to dispose SecureSecret.");

        if (cleanupFailure is not null)
            throw cleanupFailure;

        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        ThrowIfDisposingFromActiveCallback();

        if (!TryDisposeCore(_lockTimeout, out Exception? cleanupFailure))
            Environment.FailFast("SecureSecret.Dispose could not acquire the master lock within the configured timeout.");

        if (cleanupFailure is not null)
            throw cleanupFailure;

        GC.SuppressFinalize(this);
    }

    ~SecureSecret()
    {
        try
        {
            _disposed = true;
            NativeSecretMemory? masterToDispose = Interlocked.Exchange(ref _master, null);
            masterToDispose?.Dispose();
        }
        catch (Exception ex)
        {
            Environment.FailFast("SecureSecret finalizer cleanup failed.", ex);
        }
    }

    private bool TryDisposeCore(TimeSpan timeout, out Exception? cleanupFailure)
    {
        cleanupFailure = null;
        bool lockTaken = false;

        try
        {
            if (!Monitor.TryEnter(_gate, timeout))
                return false;

            lockTaken = true;

            if (_master is null)
            {
                _disposed = true;
                return true;
            }

            NativeSecretMemory masterToDispose = _master;
            _master = null;
            _disposed = true;

            try
            {
                masterToDispose.Dispose();
            }
            catch (Exception ex)
            {
                cleanupFailure = ex;
            }

            return true;
        }
        finally
        {
            if (lockTaken)
                Monitor.Exit(_gate);
        }
    }

    private void ThrowIfDisposingFromActiveCallback()
    {
        int currentThreadId = Environment.CurrentManagedThreadId;
        lock (_callbackThreadsGate)
        {
            if (_activeCallbackThreads.ContainsKey(currentThreadId))
                throw new InvalidOperationException("SecureSecret cannot be disposed from inside an active snapshot callback on the same thread.");
        }
    }

    private static void ValidateTimeout(TimeSpan timeout, string paramName)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(paramName);
    }

    private void HandleMasterReprotectFailure(
        NativeSecretMemory master,
        NativeSecretMemory? snapshot,
        Exception protectFailure)
    {
        _master = null;
        _disposed = true;

        Exception? snapshotCleanupFailure = null;
        try
        {
            snapshot?.Dispose();
        }
        catch (Exception ex)
        {
            snapshotCleanupFailure = ex;
        }

        try
        {
            master.Dispose();
        }
        catch (Exception cleanupException)
        {
            if (snapshotCleanupFailure is not null)
                cleanupException = new AggregateException(snapshotCleanupFailure, cleanupException);

            Environment.FailFast("SecureSecret master cleanup failed after re-protection failure.", cleanupException);
        }

        if (snapshotCleanupFailure is not null)
            Environment.FailFast(
                "SecureSecret snapshot cleanup failed after master re-protection failure.",
                new AggregateException(protectFailure, snapshotCleanupFailure));

        throw new InvalidOperationException(
            "SecureSecret master re-protection failed. The secret has been disposed.",
            protectFailure);
    }
}
