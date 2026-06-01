using System.Runtime.InteropServices;
using System.Text;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class ProtectedStringTests
{
    [Fact]
    public void Constructor_WhenInitialProtectRestoreFails_CleansUpAllocatedBuffer()
    {
        var api = new RecordingProtectedMemoryApi
        {
            FailProtectOnCall = 2
        };

        Assert.Throws<InvalidOperationException>(() =>
            new ProtectedString("Hello".AsSpan(), protect: true, api, TimeSpan.FromSeconds(1)));

        Assert.NotNull(api.LastFreedBytes);
        Assert.All(api.LastFreedBytes!, value => Assert.Equal(0, value));
    }

    [Fact]
    public void AppendChar_WhenReprotectFails_DisposesBufferAndStringObservesDisposed()
    {
        var api = new RecordingProtectedMemoryApi
        {
            FailProtectOnCall = 2
        };
        using var ps = new ProtectedString(ReadOnlySpan<char>.Empty, protect: true, api, TimeSpan.FromSeconds(1));

        Assert.Throws<InvalidOperationException>(() => ps.AppendChar('A'));

        Assert.NotNull(api.LastFreedBytes);
        Assert.All(api.LastFreedBytes!, value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => ps.Length);
        Assert.Throws<ObjectDisposedException>(() => ps.UseUtf16BytesSnapshot(_ => { }));
    }

    [Fact]
    public void AppendChar_IncreasesLength()
    {
        using var ps = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);

        ps.AppendChar('A');
        ps.AppendChar('B');
        ps.AppendChar('C');

        AssertContent(ps, "ABC");
    }

    [Theory]
    [InlineData("BC", 0, 'A', "ABC")]
    [InlineData("AC", 1, 'B', "ABC")]
    [InlineData("AB", 2, 'C', "ABC")]
    public void InsertAt_InsertsCharAtPosition(string initial, int index, char ch, string expected)
    {
        using var ps = new ProtectedString(initial.AsSpan(), protect: false);

        ps.InsertAt(index, ch);

        AssertContent(ps, expected);
    }

    [Theory]
    [InlineData("ABC", 0, "BC")]
    [InlineData("ABC", 1, "AC")]
    [InlineData("ABC", 2, "AB")]
    public void RemoveAt_RemovesCharAtPosition(string initial, int index, string expected)
    {
        using var ps = new ProtectedString(initial.AsSpan(), protect: false);

        ps.RemoveAt(index);

        AssertContent(ps, expected);
    }

    [Theory]
    [InlineData("AXC", 1, 'B', "ABC")]
    [InlineData("XBC", 0, 'A', "ABC")]
    [InlineData("ABX", 2, 'C', "ABC")]
    public void SetAt_ReplacesCharAtPosition(string initial, int index, char ch, string expected)
    {
        using var ps = new ProtectedString(initial.AsSpan(), protect: false);

        ps.SetAt(index, ch);

        AssertContent(ps, expected);
    }

    [Fact]
    public void SetFromUtf16Bytes_ReplacesContent()
    {
        using var ps = new ProtectedString("old".AsSpan(), protect: false);

        ps.SetFromUtf16Bytes(Encoding.Unicode.GetBytes("new value"));

        AssertContent(ps, "new value");
    }

    [Fact]
    public void SetFromUtf16Bytes_InvalidByteLength_ThrowsAndLeavesContentUnchanged()
    {
        using var ps = new ProtectedString("keep".AsSpan(), protect: false);

        Assert.Throws<ArgumentException>(() => ps.SetFromUtf16Bytes([0x41]));
        AssertContent(ps, "keep");
    }

    [Fact]
    public void SetFromUtf16Bytes_ReadOnlyInstance_ThrowsAndLeavesContentUnchanged()
    {
        using var ps = new ProtectedString("keep".AsSpan(), protect: false);
        ps.MakeReadOnly();

        Assert.Throws<InvalidOperationException>(() => ps.SetFromUtf16Bytes(Encoding.Unicode.GetBytes("new")));
        AssertContent(ps, "keep");
    }

    [Fact]
    public void Copy_CreatesDeepCopy()
    {
        using var original = new ProtectedString("Hello".AsSpan(), protect: false);
        original.MakeReadOnly();

        using var copy = original.Copy();

        Assert.False(copy.IsReadOnly);
        Assert.Equal(5, copy.Length);

        copy.AppendChar('!');
        Assert.Equal(5, original.Length);
        AssertContent(copy, "Hello!");
    }

    [Fact]
    public void MakeReadOnly_BlocksMutations()
    {
        using var ps = new ProtectedString("Test".AsSpan(), protect: false);
        ps.MakeReadOnly();

        Assert.True(ps.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => ps.AppendChar('X'));
        Assert.Throws<InvalidOperationException>(() => ps.InsertAt(0, 'X'));
        Assert.Throws<InvalidOperationException>(() => ps.RemoveAt(0));
        Assert.Throws<InvalidOperationException>(() => ps.SetAt(0, 'X'));
        Assert.Throws<InvalidOperationException>(() => ps.Clear());
        Assert.Throws<InvalidOperationException>(() => ps.SetFromUtf16Bytes(Encoding.Unicode.GetBytes("X")));
    }

    [Fact]
    public void Dispose_PreventsFurtherOperations()
    {
        var ps = new ProtectedString("Test".AsSpan(), protect: false);
        ps.Dispose();

        Assert.Throws<ObjectDisposedException>(() => ps.Length);
        Assert.Throws<ObjectDisposedException>(() => ps.AppendChar('X'));
        Assert.Throws<ObjectDisposedException>(() => ps.MakeReadOnly());
        Assert.Throws<ObjectDisposedException>(() => ps.IsReadOnly);
        Assert.Throws<ObjectDisposedException>(() => ps.Copy());
        Assert.Throws<ObjectDisposedException>(() => ps.UseUtf16BytesSnapshot(_ => { }));
        Assert.Throws<ObjectDisposedException>(() => ps.UseUnicodeSnapshot(_ => { }));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var ps = new ProtectedString("Test".AsSpan(), protect: false);
        ps.Dispose();
        ps.Dispose();
    }

    [Fact]
    public void CapacityGrowth_AcrossBlockBoundary()
    {
        using var ps = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);

        for (int i = 0; i < 9; i++)
            ps.AppendChar((char)('A' + i));

        AssertContent(ps, "ABCDEFGHI");
    }

    [Fact]
    public void UseUtf16BytesSnapshot_ExposesLogicalBytesOnly()
    {
        using var ps = new ProtectedString("P@ss".AsSpan(), protect: false);

        byte[] bytes = ps.UseUtf16BytesSnapshot(data => data.ToArray());

        Assert.Equal(8, bytes.Length);
        Assert.Equal("P@ss", Encoding.Unicode.GetString(bytes));
    }

    [Fact]
    public void UseUnicodeSnapshot_ExposesPointerAndCharCount()
    {
        using var ps = new ProtectedString("P@ss".AsSpan(), protect: false);

        string text = ps.UseUnicodeSnapshot(snapshot =>
            Marshal.PtrToStringUni(snapshot.DangerousGetIntPtr(), snapshot.CharCount) ?? string.Empty);

        Assert.Equal("P@ss", text);
    }

    [Fact]
    public void UseUtf16BytesSnapshot_RemainsStableWhileMasterMutates()
    {
        using var ps = new ProtectedString("hello".AsSpan(), protect: false);
        using var snapshotEntered = new ManualResetEventSlim();
        using var mutationCompleted = new ManualResetEventSlim();
        string? observed = null;

        var thread = new Thread(() =>
        {
            observed = ps.UseUtf16BytesSnapshot(bytes =>
            {
                snapshotEntered.Set();
                Assert.True(mutationCompleted.Wait(TimeSpan.FromSeconds(5)));
                return Encoding.Unicode.GetString(bytes);
            });
        })
        {
            IsBackground = true
        };

        thread.Start();
        Assert.True(snapshotEntered.Wait(TimeSpan.FromSeconds(5)));

        ps.SetFromUtf16Bytes(Encoding.Unicode.GetBytes("world"));
        mutationCompleted.Set();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Equal("hello", observed);
        AssertContent(ps, "world");
    }

    [Fact]
    public void UseUnicodeSnapshot_RemainsStableWhileMasterDisposes()
    {
        var ps = new ProtectedString("hello".AsSpan(), protect: false);
        using var snapshotEntered = new ManualResetEventSlim();
        using var disposeCompleted = new ManualResetEventSlim();
        string? observed = null;

        var thread = new Thread(() =>
        {
            observed = ps.UseUnicodeSnapshot(snapshot =>
            {
                snapshotEntered.Set();
                Assert.True(disposeCompleted.Wait(TimeSpan.FromSeconds(5)));
                return Marshal.PtrToStringUni(snapshot.DangerousGetIntPtr(), snapshot.CharCount) ?? string.Empty;
            });
        })
        {
            IsBackground = true
        };

        thread.Start();
        Assert.True(snapshotEntered.Wait(TimeSpan.FromSeconds(5)));

        ps.Dispose();
        disposeCompleted.Set();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Equal("hello", observed);
    }

    [Theory]
    [InlineData(ReturnKind.Task)]
    [InlineData(ReturnKind.GenericTask)]
    [InlineData(ReturnKind.ValueTask)]
    [InlineData(ReturnKind.GenericValueTask)]
    [InlineData(ReturnKind.IntPtr)]
    public void SnapshotApis_RejectUnsupportedReturnTypes(ReturnKind kind)
    {
        using var ps = new ProtectedString("hello".AsSpan(), protect: false);

        Assert.Throws<NotSupportedException>(() => InvokeUnsupportedUtf16Return(ps, kind));
        Assert.Throws<NotSupportedException>(() => InvokeUnsupportedUnicodeReturn(ps, kind));
    }

    [Fact]
    public void SnapshotTimeout_ThrowsBeforeSecondCallbackRuns()
    {
        var api = new RecordingProtectedMemoryApi();
        using var firstCopyEntered = new ManualResetEventSlim();
        using var releaseFirstCopy = new ManualResetEventSlim();
        api.BeforeCopy = () =>
        {
            firstCopyEntered.Set();
            Assert.True(releaseFirstCopy.Wait(TimeSpan.FromSeconds(5)));
        };

        using var ps = new ProtectedString("hello".AsSpan(), protect: true, api, TimeSpan.FromMilliseconds(50));
        bool secondCallbackRan = false;

        var thread = new Thread(() => ps.UseUtf16BytesSnapshot(_ => { }))
        {
            IsBackground = true
        };

        thread.Start();
        Assert.True(firstCopyEntered.Wait(TimeSpan.FromSeconds(5)));

        Assert.Throws<TimeoutException>(() => ps.UseUtf16BytesSnapshot(_ => secondCallbackRan = true));
        Assert.False(secondCallbackRan);

        releaseFirstCopy.Set();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void InsertAt_InvalidIndex_Throws(int index)
    {
        using var ps = new ProtectedString("Test".AsSpan(), protect: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => ps.InsertAt(index, 'X'));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void RemoveAt_InvalidIndex_Throws(int index)
    {
        using var ps = new ProtectedString("Test".AsSpan(), protect: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => ps.RemoveAt(index));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void SetAt_InvalidIndex_Throws(int index)
    {
        using var ps = new ProtectedString("Test".AsSpan(), protect: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => ps.SetAt(index, 'X'));
    }

    [Fact]
    public void ContentEqual_BothNull_ReturnsTrue()
    {
        Assert.True(ProtectedString.ContentEqual(null, null));
    }

    [Fact]
    public void ContentEqual_OneNull_ReturnsFalse()
    {
        using var ps = new ProtectedString("abc".AsSpan(), protect: false);
        Assert.False(ProtectedString.ContentEqual(ps, null));
        Assert.False(ProtectedString.ContentEqual(null, ps));
    }

    [Fact]
    public void ContentEqual_DifferentLengths_ReturnsFalse()
    {
        using var a = new ProtectedString("abc".AsSpan(), protect: false);
        using var b = new ProtectedString("abcd".AsSpan(), protect: false);
        Assert.False(ProtectedString.ContentEqual(a, b));
    }

    [Fact]
    public void ContentEqual_SameContent_ReturnsTrue()
    {
        using var a = new ProtectedString("P@ssw0rd".AsSpan(), protect: false);
        using var b = new ProtectedString("P@ssw0rd".AsSpan(), protect: false);
        Assert.True(ProtectedString.ContentEqual(a, b));
    }

    [Fact]
    public void ContentEqual_DifferentContentSameLength_ReturnsFalse()
    {
        using var a = new ProtectedString("Pass1234".AsSpan(), protect: false);
        using var b = new ProtectedString("Pass1235".AsSpan(), protect: false);
        Assert.False(ProtectedString.ContentEqual(a, b));
    }

    [Fact]
    public void ContentEqual_BothEmpty_ReturnsTrue()
    {
        using var a = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);
        using var b = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);
        Assert.True(ProtectedString.ContentEqual(a, b));
    }

    [Fact]
    public void CharAt_ReturnsCorrectChar()
    {
        using var ps = new ProtectedString("Hello".AsSpan(), protect: false);

        Assert.Equal('H', ps.CharAt(0));
        Assert.Equal('e', ps.CharAt(1));
        Assert.Equal('l', ps.CharAt(2));
        Assert.Equal('l', ps.CharAt(3));
        Assert.Equal('o', ps.CharAt(4));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void CharAt_OutOfRange_Throws(int index)
    {
        using var ps = new ProtectedString("Hello".AsSpan(), protect: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => ps.CharAt(index));
    }

    [Fact]
    public void CharAt_SurrogatePair_ReturnsBothHalves()
    {
        const char high = '\uD83D';
        const char low = '\uDD11';
        using var ps = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);
        ps.InsertAt(0, high);
        ps.InsertAt(1, low);

        Assert.Equal(2, ps.Length);
        Assert.Equal(high, ps.CharAt(0));
        Assert.Equal(low, ps.CharAt(1));
    }

    [Fact]
    public void InsertAt_SurrogatePairAtomically_TwoCodeUnits()
    {
        const char high = '\uD83D';
        const char low = '\uDD11';
        using var ps = new ProtectedString("AB".AsSpan(), protect: false);
        ps.InsertAt(1, high);
        ps.InsertAt(2, low);

        Assert.Equal(4, ps.Length);
        Assert.Equal('A', ps.CharAt(0));
        Assert.Equal(high, ps.CharAt(1));
        Assert.Equal(low, ps.CharAt(2));
        Assert.Equal('B', ps.CharAt(3));
    }

    [Fact]
    public void RemoveAt_SurrogatePairTwice_RemovesBothCodeUnits()
    {
        const char high = '\uD83D';
        const char low = '\uDD11';
        using var ps = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);
        ps.InsertAt(0, high);
        ps.InsertAt(1, low);

        ps.RemoveAt(0);
        ps.RemoveAt(0);

        Assert.Equal(0, ps.Length);
    }

    private static void AssertContent(ProtectedString ps, string expected) =>
        Assert.Equal(expected, ps.UseUnicodeSnapshot(snapshot =>
            Marshal.PtrToStringUni(snapshot.DangerousGetIntPtr(), snapshot.CharCount) ?? string.Empty));

    private static object? InvokeUnsupportedUtf16Return(ProtectedString ps, ReturnKind kind)
    {
        switch (kind)
        {
            case ReturnKind.Task:
                return ps.UseUtf16BytesSnapshot(_ => Task.CompletedTask);
            case ReturnKind.GenericTask:
                return ps.UseUtf16BytesSnapshot(_ => Task.FromResult(1));
            case ReturnKind.ValueTask:
                return ps.UseUtf16BytesSnapshot(_ => ValueTask.CompletedTask).AsTask();
            case ReturnKind.GenericValueTask:
                return ps.UseUtf16BytesSnapshot(_ => ValueTask.FromResult(1)).AsTask();
            case ReturnKind.IntPtr:
                return ps.UseUtf16BytesSnapshot(_ => IntPtr.Zero);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static object? InvokeUnsupportedUnicodeReturn(ProtectedString ps, ReturnKind kind)
    {
        switch (kind)
        {
            case ReturnKind.Task:
                return ps.UseUnicodeSnapshot(_ => Task.CompletedTask);
            case ReturnKind.GenericTask:
                return ps.UseUnicodeSnapshot(_ => Task.FromResult(1));
            case ReturnKind.ValueTask:
                return ps.UseUnicodeSnapshot(_ => ValueTask.CompletedTask).AsTask();
            case ReturnKind.GenericValueTask:
                return ps.UseUnicodeSnapshot(_ => ValueTask.FromResult(1)).AsTask();
            case ReturnKind.IntPtr:
                return ps.UseUnicodeSnapshot(_ => IntPtr.Zero);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    public enum ReturnKind
    {
        Task,
        GenericTask,
        ValueTask,
        GenericValueTask,
        IntPtr
    }

    private sealed class RecordingProtectedMemoryApi : IProtectedMemoryApi
    {
        private readonly Dictionary<IntPtr, int> _allocations = [];

        public Action? BeforeCopy { get; set; }
        public int? FailProtectOnCall { get; set; }
        public int ProtectCalls { get; private set; }
        public byte[]? LastFreedBytes { get; private set; }

        public IntPtr Allocate(int byteCount)
        {
            IntPtr address = Marshal.AllocHGlobal(byteCount);
            _allocations[address] = byteCount;
            return address;
        }

        public void Free(IntPtr address)
        {
            int byteCount = _allocations[address];
            LastFreedBytes = new byte[byteCount];
            Marshal.Copy(address, LastFreedBytes, 0, byteCount);
            Marshal.FreeHGlobal(address);
            _allocations.Remove(address);
        }

        public bool VirtualLock(IntPtr address, int byteCount) => true;

        public bool VirtualUnlock(IntPtr address, int byteCount) => true;

        public bool CryptProtectMemory(IntPtr address, int byteCount)
        {
            ProtectCalls++;
            if (FailProtectOnCall == ProtectCalls)
                throw new InvalidOperationException("Protect failed.");

            return true;
        }

        public bool CryptUnprotectMemory(IntPtr address, int byteCount) => true;

        public void ZeroMemory(IntPtr address, int byteCount)
        {
            for (int i = 0; i < byteCount; i++)
                Marshal.WriteByte(address, i, 0);
        }

        public void CopyMemory(IntPtr source, IntPtr destination, int byteCount)
        {
            BeforeCopy?.Invoke();
            byte[] bytes = new byte[byteCount];
            Marshal.Copy(source, bytes, 0, byteCount);
            Marshal.Copy(bytes, 0, destination, byteCount);
        }
    }
}
