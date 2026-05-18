using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RunFence.Core;

/// <summary>
/// Stores a mutable UTF-16 sequence in protected native storage.
/// </summary>
public sealed class ProtectedString : IDisposable
{
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(1);
    private static readonly HashSet<Type> NoAdditionalRejectedReturnTypes = [];
    private static readonly HashSet<Type> RejectedUnicodeSnapshotReturnTypes =
    [
        typeof(ProtectedUtf16ZSnapshot)
    ];

    private readonly object _gate = new();
    private readonly IProtectedMemoryApi _bufferMemoryApi;
    private readonly IProtectedMemoryApi _rawProtectedMemoryApi;
    private readonly bool _useProtection;
    private readonly TimeSpan _lockTimeout;
    private ProtectedStringNativeBuffer? _buffer;
    private int _charCount;
    private bool _isReadOnly;
    private bool _disposed;

    public ProtectedString() : this(ReadOnlySpan<char>.Empty, true, NativeProtectedMemoryApi.Instance, null)
    {
    }

    public ProtectedString(ReadOnlySpan<char> chars) : this(chars, true, NativeProtectedMemoryApi.Instance, null)
    {
    }

    internal ProtectedString(ReadOnlySpan<char> chars, bool protect)
        : this(chars, protect, NativeProtectedMemoryApi.Instance, null)
    {
    }

    internal ProtectedString(
        ReadOnlySpan<char> chars,
        bool protect,
        IProtectedMemoryApi protectedMemoryApi,
        TimeSpan? lockTimeout)
    {
        _rawProtectedMemoryApi = protectedMemoryApi ?? throw new ArgumentNullException(nameof(protectedMemoryApi));
        _useProtection = protect;
        _lockTimeout = lockTimeout ?? DefaultLockTimeout;
        _bufferMemoryApi = protect
            ? _rawProtectedMemoryApi
            : new UnprotectedMemoryApi(_rawProtectedMemoryApi);
        ValidateTimeout(_lockTimeout, nameof(lockTimeout));
        try
        {
            _buffer = new ProtectedStringNativeBuffer(_bufferMemoryApi, protect);
            if (!chars.IsEmpty)
                SetFromUtf16Bytes(MemoryMarshal.AsBytes(chars));
        }
        catch
        {
            ProtectedStringNativeBuffer? bufferToDispose = _buffer;
            _buffer = null;
            bufferToDispose?.Dispose();
            throw;
        }
    }

    public int Length
    {
        get
        {
            return WithGate(
                "read the protected string length.",
                () =>
                {
                    ThrowIfDisposed();
                    return _charCount;
                });
        }
    }

    public bool IsReadOnly
    {
        get
        {
            return WithGate(
                "read the protected string read-only state.",
                () =>
                {
                    ThrowIfDisposed();
                    return _isReadOnly;
                });
        }
    }

    public void MakeReadOnly()
    {
        WithGate(
            "mark the protected string read-only.",
            () =>
            {
                ThrowIfDisposed();
                _isReadOnly = true;
            });
    }

    public void AppendChar(char c)
    {
        WithWritableBuffer(
            "append a character to the protected string.",
            buffer =>
            {
                buffer.EnsureCapacity((_charCount + 1) * sizeof(char));
                buffer.WithUnprotectedAccess(access =>
                {
                    Marshal.WriteInt16(access.Address, _charCount * sizeof(char), (short)c);
                    _charCount++;
                });
            });
    }

    public void InsertAt(int index, char c)
    {
        WithWritableBuffer(
            "insert a character into the protected string.",
            buffer =>
            {
                if (index < 0 || index > _charCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                buffer.EnsureCapacity((_charCount + 1) * sizeof(char));
                buffer.WithUnprotectedAccess(access =>
                {
                    int shiftStart = index * sizeof(char);
                    int shiftEnd = _charCount * sizeof(char);
                    for (int i = shiftEnd - 1; i >= shiftStart; i--)
                        Marshal.WriteByte(access.Address, i + sizeof(char), Marshal.ReadByte(access.Address, i));

                    Marshal.WriteInt16(access.Address, shiftStart, (short)c);
                    _charCount++;
                });
            });
    }

    public void RemoveAt(int index)
    {
        WithWritableBuffer(
            "remove a character from the protected string.",
            buffer =>
            {
                if (index < 0 || index >= _charCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                buffer.WithUnprotectedAccess(access =>
                {
                    int shiftStart = (index + 1) * sizeof(char);
                    int shiftEnd = _charCount * sizeof(char);
                    for (int i = shiftStart; i < shiftEnd; i++)
                        Marshal.WriteByte(access.Address, i - sizeof(char), Marshal.ReadByte(access.Address, i));

                    Marshal.WriteInt16(access.Address, (_charCount - 1) * sizeof(char), 0);
                    _charCount--;
                });
            });
    }

    public void SetAt(int index, char c)
    {
        WithWritableBuffer(
            "update a character in the protected string.",
            buffer =>
            {
                if (index < 0 || index >= _charCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                buffer.WithUnprotectedAccess(access =>
                    Marshal.WriteInt16(access.Address, index * sizeof(char), (short)c));
            });
    }

    public char CharAt(int index)
    {
        return WithGate(
            "read a character from the protected string.",
            () =>
            {
                ThrowIfDisposed();
                if (index < 0 || index >= _charCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return GetRequiredBuffer().WithUnprotectedAccess(access =>
                    (char)Marshal.ReadInt16(access.Address, index * sizeof(char)));
            });
    }

    public void Clear()
    {
        WithWritableBuffer(
            "clear the protected string.",
            buffer =>
            {
                buffer.Clear();
                _charCount = 0;
            });
    }

    public void SetFromUtf16Bytes(ReadOnlySpan<byte> utf16Bytes)
    {
        if ((utf16Bytes.Length & 1) != 0)
            throw new ArgumentException("UTF-16 byte input must have an even length.", nameof(utf16Bytes));

        bool lockTaken = false;
        try
        {
            if (!Monitor.TryEnter(_gate, _lockTimeout))
                throw new TimeoutException("Timed out waiting to replace the protected string from UTF-16 bytes.");

            lockTaken = true;
            ThrowIfDisposedOrReadOnly();

            ProtectedStringNativeBuffer buffer = GetRequiredBuffer();
            buffer.ReplaceContent(utf16Bytes);
            _charCount = utf16Bytes.Length / sizeof(char);
        }
        finally
        {
            if (lockTaken)
                Monitor.Exit(_gate);
        }
    }

    public ProtectedString Copy()
    {
        return WithGate(
            "copy the protected string.",
            () =>
            {
                ThrowIfDisposed();

                var copy = new ProtectedString(ReadOnlySpan<char>.Empty, _useProtection, _rawProtectedMemoryApi, _lockTimeout);
                try
                {
                    int byteLength = _charCount * sizeof(char);
                    copy._buffer!.EnsureCapacity(byteLength);
                    GetRequiredBuffer().CopyTo(copy._buffer!, byteLength);

                    copy._charCount = _charCount;
                    return copy;
                }
                catch
                {
                    copy.Dispose();
                    throw;
                }
            });
    }

    public static ProtectedString FromChars(char[] chars)
    {
        var result = new ProtectedString(chars.AsSpan());
        result.MakeReadOnly();
        return result;
    }

    public static ProtectedString FromChars(ReadOnlySpan<char> chars)
    {
        var result = new ProtectedString(chars);
        result.MakeReadOnly();
        return result;
    }

    public static ProtectedString CreateEmpty()
    {
        var result = new ProtectedString();
        result.MakeReadOnly();
        return result;
    }

    public static bool ContentEqual(ProtectedString? a, ProtectedString? b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        if (a.Length != b.Length)
            return false;

        return a.UseUtf16BytesSnapshot(bytesA =>
        {
            unsafe
            {
                fixed (byte* leftPointer = bytesA)
                {
                    IntPtr leftAddress = (IntPtr)leftPointer;
                    int byteLength = bytesA.Length;
                    return b.UseUtf16BytesSnapshot(bytesB =>
                        CryptographicOperations.FixedTimeEquals(
                            new ReadOnlySpan<byte>(leftAddress.ToPointer(), byteLength),
                            bytesB));
                }
            }
        });
    }

    public void UseUtf16BytesSnapshot(ProtectedStringBytesAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        UseUtf16BytesSnapshot(
            data =>
            {
                action(data);
                return default(VoidStruct);
            });
    }

    public T UseUtf16BytesSnapshot<T>(ProtectedStringBytesFunc<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        SecretSnapshotCallbackValidator.RejectUnsupportedReturnType<T>(
            "ProtectedString byte snapshots",
            NoAdditionalRejectedReturnTypes);

        NativeSecretMemory? snapshot = null;
        int byteLength = 0;
        bool lockTaken = false;

        try
        {
            if (!Monitor.TryEnter(_gate, _lockTimeout))
                throw new TimeoutException("Timed out waiting to create a protected string byte snapshot.");

            lockTaken = true;
            ThrowIfDisposed();

            ProtectedStringNativeBuffer buffer = GetRequiredBuffer();
            byteLength = _charCount * sizeof(char);
            snapshot = new NativeSecretMemory(Math.Max(1, byteLength), _bufferMemoryApi);
            try
            {
                buffer.WithUnprotectedAccess(access =>
                {
                    if (byteLength > 0)
                        _bufferMemoryApi.CopyMemory(access.Address, snapshot.Address, byteLength);
                });
            }
            catch
            {
                snapshot.Dispose();
                snapshot = null;
                throw;
            }

            Monitor.Exit(_gate);
            lockTaken = false;

            unsafe
            {
                return action(new ReadOnlySpan<byte>(snapshot.Address.ToPointer(), byteLength));
            }
        }
        finally
        {
            snapshot?.Dispose();
            if (lockTaken)
                Monitor.Exit(_gate);
        }
    }

    public void UseUnicodeSnapshot(ProtectedStringUnicodeAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        UseUnicodeSnapshot(
            snapshot =>
            {
                action(snapshot);
                return default(VoidStruct);
            });
    }

    public T UseUnicodeSnapshot<T>(ProtectedStringUnicodeFunc<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        SecretSnapshotCallbackValidator.RejectUnsupportedReturnType<T>(
            "ProtectedString Unicode snapshots",
            RejectedUnicodeSnapshotReturnTypes);

        NativeSecretMemory? snapshot = null;
        int charCount = 0;
        bool lockTaken = false;

        try
        {
            if (!Monitor.TryEnter(_gate, _lockTimeout))
                throw new TimeoutException("Timed out waiting to create a protected string Unicode snapshot.");

            lockTaken = true;
            ThrowIfDisposed();

            ProtectedStringNativeBuffer buffer = GetRequiredBuffer();
            charCount = _charCount;
            int logicalByteLength = charCount * sizeof(char);
            snapshot = new NativeSecretMemory(Math.Max(1, logicalByteLength + sizeof(char)), _bufferMemoryApi);
            try
            {
                buffer.WithUnprotectedAccess(access =>
                {
                    if (logicalByteLength > 0)
                        _bufferMemoryApi.CopyMemory(access.Address, snapshot.Address, logicalByteLength);

                    Marshal.WriteInt16(snapshot.Address, logicalByteLength, 0);
                });
            }
            catch
            {
                snapshot.Dispose();
                snapshot = null;
                throw;
            }

            Monitor.Exit(_gate);
            lockTaken = false;
            return action(new ProtectedUtf16ZSnapshot(charCount, snapshot.Address));
        }
        finally
        {
            snapshot?.Dispose();
            if (lockTaken)
                Monitor.Exit(_gate);
        }
    }

    public void Dispose()
    {
        bool lockTaken = false;
        try
        {
            if (!Monitor.TryEnter(_gate, _lockTimeout))
                Environment.FailFast("ProtectedString.Dispose could not acquire the string lock within the configured timeout.");

            lockTaken = true;
            if (_disposed)
                return;

            _disposed = true;
            ProtectedStringNativeBuffer? bufferToDispose = _buffer;
            _buffer = null;
            bufferToDispose?.Dispose();
        }
        finally
        {
            if (lockTaken)
                Monitor.Exit(_gate);
        }

        GC.SuppressFinalize(this);
    }

    ~ProtectedString()
    {
        try
        {
            _disposed = true;
            ProtectedStringNativeBuffer? bufferToDispose = Interlocked.Exchange(ref _buffer, null);
            bufferToDispose?.Dispose();
        }
        catch (Exception ex)
        {
            Environment.FailFast("ProtectedString finalizer cleanup failed.", ex);
        }
    }

    private T WithGate<T>(string timeoutMessage, Func<T> action)
    {
        if (!Monitor.TryEnter(_gate, _lockTimeout))
            throw new TimeoutException($"Timed out waiting to {timeoutMessage}");

        try
        {
            return action();
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private void WithGate(string timeoutMessage, Action action) =>
        WithGate(
            timeoutMessage,
            () =>
            {
                action();
                return default(VoidStruct);
            });

    private void WithWritableBuffer(string timeoutMessage, Action<ProtectedStringNativeBuffer> action) =>
        WithGate(
            timeoutMessage,
            () =>
            {
                ThrowIfDisposedOrReadOnly();
                action(GetRequiredBuffer());
            });

    private ProtectedStringNativeBuffer GetRequiredBuffer() =>
        _buffer is { IsDisposed: false } buffer
            ? buffer
            : throw new ObjectDisposedException(nameof(ProtectedString));

    private void ThrowIfDisposed()
    {
        if (_disposed || _buffer?.IsDisposed == true)
            throw new ObjectDisposedException(nameof(ProtectedString));
    }

    private void ThrowIfDisposedOrReadOnly()
    {
        ThrowIfDisposed();
        if (_isReadOnly)
            throw new InvalidOperationException("ProtectedString is read-only.");
    }

    private static void ValidateTimeout(TimeSpan timeout, string paramName)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(paramName);
    }
}
