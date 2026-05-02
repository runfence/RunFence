using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RunFence.Core;

/// <summary>
/// Stores a mutable UTF-16 sequence in a ProtectedMemoryBlock.
/// Characters are only unprotected during mutation/export operations.
/// NOT thread-safe.
/// </summary>
public sealed class ProtectedString : IDisposable
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SysAllocStringLen(IntPtr psz, uint len);

    private readonly bool _useProtection;
    private ProtectedMemoryBlock _block;
    private int _charCount;
    private bool _isReadOnly;
    private bool _disposed;

    public ProtectedString() : this(ReadOnlySpan<char>.Empty, true)
    {
    }

    public ProtectedString(ReadOnlySpan<char> chars) : this(chars, true)
    {
    }

    internal ProtectedString(ReadOnlySpan<char> chars, bool protect)
    {
        _useProtection = protect;
        _block = new ProtectedMemoryBlock(RequiredByteCapacity(chars.Length), protect, NativeProtectedMemoryApi.Instance);
        _charCount = chars.Length;

        if (chars.Length == 0)
            return;

        using var scope = _block.Unprotect();
        for (int i = 0; i < chars.Length; i++)
            Marshal.WriteInt16(scope.Address, i * 2, (short)chars[i]);
    }

    private ProtectedString(bool protect)
        : this(ReadOnlySpan<char>.Empty, protect)
    {
    }

    public int Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _charCount;
        }
    }

    public bool IsReadOnly
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _isReadOnly;
        }
    }

    public void MakeReadOnly()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _isReadOnly = true;
    }

    public void AppendChar(char c) => InsertAt(_charCount, c);

    public void InsertAt(int index, char c)
    {
        ThrowIfDisposedOrReadOnly();
        if (index < 0 || index > _charCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        _block.EnsureCapacity(RequiredByteCapacity(_charCount + 1));

        using var scope = _block.Unprotect();
        int shiftStart = index * 2;
        int shiftEnd = _charCount * 2;
        for (int i = shiftEnd - 1; i >= shiftStart; i--)
            Marshal.WriteByte(scope.Address, i + 2, Marshal.ReadByte(scope.Address, i));

        Marshal.WriteInt16(scope.Address, index * 2, (short)c);
        _charCount++;
    }

    public void RemoveAt(int index)
    {
        ThrowIfDisposedOrReadOnly();
        if (index < 0 || index >= _charCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        using var scope = _block.Unprotect();
        int shiftStart = (index + 1) * 2;
        int shiftEnd = _charCount * 2;
        for (int i = shiftStart; i < shiftEnd; i++)
            Marshal.WriteByte(scope.Address, i - 2, Marshal.ReadByte(scope.Address, i));

        Marshal.WriteInt16(scope.Address, (_charCount - 1) * 2, 0);
        _charCount--;
    }

    public void SetAt(int index, char c)
    {
        ThrowIfDisposedOrReadOnly();
        if (index < 0 || index >= _charCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        using var scope = _block.Unprotect();
        Marshal.WriteInt16(scope.Address, index * 2, (short)c);
    }

    public char CharAt(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (index < 0 || index >= _charCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        using var scope = _block.Unprotect();
        return (char)Marshal.ReadInt16(scope.Address, index * 2);
    }

    public void Clear()
    {
        ThrowIfDisposedOrReadOnly();

        _block.Clear();
        _charCount = 0;
    }

    public ProtectedString Copy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var copy = new ProtectedString(_useProtection);
        copy._block.EnsureCapacity(RequiredByteCapacity(_charCount));

        using var source = _block.Unprotect();
        using var target = copy._block.Unprotect();
        for (int i = 0; i < _charCount * 2; i++)
            Marshal.WriteByte(target.Address, i, Marshal.ReadByte(source.Address, i));

        copy._charCount = _charCount;
        return copy;
    }

    public IntPtr AllocUnicode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var dest = Marshal.AllocHGlobal((_charCount + 1) * 2);
        using var scope = _block.Unprotect();
        try
        {
            for (int i = 0; i < _charCount * 2; i++)
                Marshal.WriteByte(dest, i, Marshal.ReadByte(scope.Address, i));
            Marshal.WriteInt16(dest, _charCount * 2, 0);
        }
        catch
        {
            Marshal.ZeroFreeGlobalAllocUnicode(dest);
            throw;
        }

        return dest;
    }

    public IntPtr ToBSTR()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var bstr = SysAllocStringLen(IntPtr.Zero, (uint)_charCount);
        if (bstr == IntPtr.Zero)
            throw new OutOfMemoryException();

        using var scope = _block.Unprotect();
        try
        {
            for (int i = 0; i < _charCount * 2; i++)
                Marshal.WriteByte(bstr, i, Marshal.ReadByte(scope.Address, i));
        }
        catch
        {
            Marshal.ZeroFreeBSTR(bstr);
            throw;
        }

        return bstr;
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

    public static unsafe bool ContentEqual(ProtectedString? a, ProtectedString? b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        if (a._charCount != b._charCount)
            return false;

        int byteLen = a._charCount * 2;
        var ptrA = a.AllocUnicode();
        try
        {
            var ptrB = b.AllocUnicode();
            try
            {
                return CryptographicOperations.FixedTimeEquals(
                    new ReadOnlySpan<byte>(ptrA.ToPointer(), byteLen),
                    new ReadOnlySpan<byte>(ptrB.ToPointer(), byteLen));
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(ptrB);
            }
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptrA);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        GC.SuppressFinalize(this);
        _block.Dispose();
    }

    ~ProtectedString() => Dispose();

    private void ThrowIfDisposedOrReadOnly()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isReadOnly)
            throw new InvalidOperationException("ProtectedString is read-only.");
    }

    private static int RequiredByteCapacity(int charCount) =>
        Math.Max(CryptMemoryNative.CRYPTPROTECTMEMORY_BLOCK_SIZE,
            ProtectedMemoryBlock.RoundUpToBlockSize(charCount * 2));
}
