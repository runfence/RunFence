using System.ComponentModel;
using System.Runtime.InteropServices;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class SecureSecretTests
{
    [Fact]
    public void NativeSecretMemory_Dispose_ZeroesUnlocksAndFreesMemory()
    {
        var api = new RecordingProtectedMemoryApi();
        var memory = new NativeSecretMemory(18, api);

        for (int i = 0; i < 18; i++)
            Marshal.WriteByte(memory.Address, i, 0x6A);

        memory.Protect();
        Assert.True(memory.IsProtected);

        memory.Dispose();

        Assert.Equal(1, api.FreeCalls);
        Assert.Equal(1, api.VirtualUnlockCalls);
        Assert.NotNull(api.LastFreedBytes);
        Assert.Equal(32, api.LastFreedBytes!.Length);
        Assert.All(api.LastFreedBytes, value => Assert.Equal(0, value));
    }

    [Fact]
    public void NativeSecretMemory_WhenVirtualLockFails_ZeroesAndFreesAllocation()
    {
        var api = new RecordingProtectedMemoryApi
        {
            FailVirtualLock = true
        };

        Assert.ThrowsAny<Exception>(() => new NativeSecretMemory(16, api));

        Assert.Equal(1, api.FreeCalls);
        Assert.NotNull(api.LastFreedBytes);
        Assert.All(api.LastFreedBytes!, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Constructor_ZeroesFullCapacityBeforeInitializerAndProtectsMasterBeforeReturning()
    {
        var api = new RecordingProtectedMemoryApi();

        using var secret = new SecureSecret(
            18,
            data =>
            {
                Assert.Equal(18, data.Length);
                data.Fill(0x5A);
            },
            api,
            lockTimeout: TimeSpan.FromSeconds(1));

        Assert.Equal(1, api.ProtectCalls);
        Assert.NotNull(api.LastProtectedBytes);
        Assert.Equal(32, api.LastProtectedBytes!.Length);
        Assert.All(api.LastProtectedBytes.Take(18), value => Assert.Equal(0x5A, value));
        Assert.All(api.LastProtectedBytes.Skip(18), value => Assert.Equal(0, value));
    }

    [Fact]
    public void Constructor_WhenInitializerThrows_ZeroesAndFreesNativeMemory()
    {
        var api = new RecordingProtectedMemoryApi();

        Assert.Throws<InvalidOperationException>(() => new SecureSecret(
            16,
            _ => throw new InvalidOperationException("init failed"),
            api,
            lockTimeout: TimeSpan.FromSeconds(1)));

        Assert.Equal(1, api.FreeCalls);
        Assert.NotNull(api.LastFreedBytes);
        Assert.All(api.LastFreedBytes!, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Constructor_WhenVirtualLockFails_ThrowsBeforeInitializerAndZeroesFreedMemory()
    {
        var api = new RecordingProtectedMemoryApi
        {
            FailVirtualLock = true
        };
        bool initializerCalled = false;

        Assert.ThrowsAny<Exception>(() => new SecureSecret(
            16,
            _ => initializerCalled = true,
            api,
            lockTimeout: TimeSpan.FromSeconds(1)));

        Assert.False(initializerCalled);
        Assert.Equal(1, api.FreeCalls);
        Assert.NotNull(api.LastFreedBytes);
        Assert.All(api.LastFreedBytes!, value => Assert.Equal(0, value));
    }

    [Fact]
    public void TransformSnapshot_WhenSnapshotVirtualLockFails_DoesNotInvokeCallbackAndZeroesFreedMemory()
    {
        var api = new RecordingProtectedMemoryApi();
        using var secret = new SecureSecret(16, data => data.Fill(0x3C), api, TimeSpan.FromSeconds(1));
        api.FailVirtualLock = true;

        Assert.ThrowsAny<Exception>(() => secret.TransformSnapshot<int>(_ => 7));

        Assert.Equal(1, api.FreeCalls);
        Assert.NotNull(api.LastFreedBytes);
        Assert.All(api.LastFreedBytes!, value => Assert.Equal(0, value));
    }

    [Fact]
    public void TransformSnapshot_WhenMasterReprotectFails_DisposesSecretAndZeroesFreedMasterMemory()
    {
        var api = new RecordingProtectedMemoryApi
        {
            FailProtectOnCall = 2
        };
        using var secret = new SecureSecret(16, data => data.Fill(0x4D), api, TimeSpan.FromSeconds(1));
        bool callbackInvoked = false;

        var ex = Assert.Throws<InvalidOperationException>(() => secret.TransformSnapshot<int>(_ =>
        {
            callbackInvoked = true;
            return 7;
        }));

        Assert.False(callbackInvoked);
        Assert.IsType<Win32Exception>(ex.InnerException);
        Assert.Equal(2, api.FreeCalls);
        Assert.Equal(2, api.FreedBlocks.Count);
        Assert.All(api.FreedBlocks[^1], value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => secret.TransformSnapshot(_ => 9));
    }

    [Fact]
    public void TransformSnapshot_UsesRealLengthInsteadOfPaddedCapacity()
    {
        using var secret = CreateSecret(18, 0x2D);

        secret.UseSnapshot(data => Assert.Equal(18, data.Length));
    }

    [Fact]
    public void TransformSnapshot_AllowsActiveSnapshotToSurviveMasterDispose()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var disposed = new ManualResetEventSlim();
        using var secret = CreateSecret(16, 0x44);
        byte[]? observed = null;

        var thread = new Thread(() =>
        {
            observed = secret.TransformSnapshot(data =>
            {
                entered.Set();
                Assert.True(disposed.Wait(TimeSpan.FromSeconds(5)));
                return data.ToArray();
            });
            release.Set();
        })
        {
            IsBackground = true
        };

        thread.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        secret.DisposeOrThrow(TimeSpan.FromSeconds(1));
        disposed.Set();

        Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.NotNull(observed);
        Assert.Equal(Enumerable.Repeat((byte)0x44, 16), observed);
    }

    [Fact]
    public void TransformSnapshot_ReleasesGateBeforeInvokingCallback()
    {
        using var firstEntered = new ManualResetEventSlim();
        using var secondCompleted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var secret = CreateSecret(16, 0x55);

        var firstThread = new Thread(() =>
        {
            secret.UseSnapshot(_ =>
            {
                firstEntered.Set();
                Assert.True(releaseFirst.Wait(TimeSpan.FromSeconds(5)));
            });
        })
        {
            IsBackground = true
        };

        var secondThread = new Thread(() =>
        {
            Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5)));
            secret.UseSnapshot(_ => secondCompleted.Set());
        })
        {
            IsBackground = true
        };

        firstThread.Start();
        secondThread.Start();

        Assert.True(secondCompleted.Wait(TimeSpan.FromSeconds(5)));
        releaseFirst.Set();

        Assert.True(firstThread.Join(TimeSpan.FromSeconds(5)));
        Assert.True(secondThread.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void TransformSnapshot_ConcurrentCallbacksReceiveIndependentSnapshots()
    {
        using var firstReady = new ManualResetEventSlim();
        using var secondReady = new ManualResetEventSlim();
        using var releaseBoth = new ManualResetEventSlim();
        var api = new RecordingProtectedMemoryApi();
        using var secret = new SecureSecret(16, data => data.Fill(0x61), api, TimeSpan.FromSeconds(1));

        var firstThread = new Thread(() =>
        {
            secret.UseSnapshot(data =>
            {
                firstReady.Set();
                Assert.True(releaseBoth.Wait(TimeSpan.FromSeconds(5)));
            });
        })
        {
            IsBackground = true
        };

        var secondThread = new Thread(() =>
        {
            secret.UseSnapshot(data =>
            {
                secondReady.Set();
                Assert.True(releaseBoth.Wait(TimeSpan.FromSeconds(5)));
            });
        })
        {
            IsBackground = true
        };

        firstThread.Start();
        secondThread.Start();

        Assert.True(firstReady.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(secondReady.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(api.Allocations.Count >= 3);
        Assert.NotEqual(api.Allocations[1], api.Allocations[2]);

        releaseBoth.Set();
        Assert.True(firstThread.Join(TimeSpan.FromSeconds(5)));
        Assert.True(secondThread.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void TransformSnapshot_TimeoutThrowsBeforeASecondCallCanUnprotectMaster()
    {
        var api = new RecordingProtectedMemoryApi();
        using var enteredCopy = new ManualResetEventSlim();
        using var releaseCopy = new ManualResetEventSlim();
        api.BeforeCopy = () =>
        {
            enteredCopy.Set();
            Assert.True(releaseCopy.Wait(TimeSpan.FromSeconds(5)));
        };

        using var secret = new SecureSecret(16, data => data.Fill(0x10), api, TimeSpan.FromMilliseconds(50));

        var thread = new Thread(() => secret.UseSnapshot(_ => { }))
        {
            IsBackground = true
        };

        thread.Start();
        Assert.True(enteredCopy.Wait(TimeSpan.FromSeconds(5)));

        var ex = Assert.Throws<TimeoutException>(() => secret.UseSnapshot(_ => { }));
        Assert.Contains("Timed out", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, api.UnprotectCalls);

        releaseCopy.Set();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void TryDispose_ReturnsFalseWhenMasterLockCannotBeAcquiredWithinTimeout()
    {
        var api = new RecordingProtectedMemoryApi();
        using var enteredCopy = new ManualResetEventSlim();
        using var releaseCopy = new ManualResetEventSlim();
        api.BeforeCopy = () =>
        {
            enteredCopy.Set();
            Assert.True(releaseCopy.Wait(TimeSpan.FromSeconds(5)));
        };

        using var secret = new SecureSecret(16, data => data.Fill(0x20), api, TimeSpan.FromSeconds(1));
        var thread = new Thread(() => secret.UseSnapshot(_ => { }))
        {
            IsBackground = true
        };

        thread.Start();
        Assert.True(enteredCopy.Wait(TimeSpan.FromSeconds(5)));

        Assert.False(secret.TryDispose(TimeSpan.FromMilliseconds(50)));

        releaseCopy.Set();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void DisposeOrThrow_ThrowsTimeoutWhenMasterLockCannotBeAcquiredWithinTimeout()
    {
        var api = new RecordingProtectedMemoryApi();
        using var enteredCopy = new ManualResetEventSlim();
        using var releaseCopy = new ManualResetEventSlim();
        api.BeforeCopy = () =>
        {
            enteredCopy.Set();
            Assert.True(releaseCopy.Wait(TimeSpan.FromSeconds(5)));
        };

        using var secret = new SecureSecret(16, data => data.Fill(0x21), api, TimeSpan.FromSeconds(1));
        var thread = new Thread(() => secret.UseSnapshot(_ => { }))
        {
            IsBackground = true
        };

        thread.Start();
        Assert.True(enteredCopy.Wait(TimeSpan.FromSeconds(5)));

        Assert.Throws<TimeoutException>(() => secret.DisposeOrThrow(TimeSpan.FromMilliseconds(50)));

        releaseCopy.Set();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void DisposeMethods_RejectSameThreadDisposalInsideActiveCallback()
    {
        using var secret = CreateSecret(16, 0x31);

        secret.UseSnapshot(_ =>
        {
            Assert.Throws<InvalidOperationException>(() => secret.TryDispose(TimeSpan.Zero));
            Assert.Throws<InvalidOperationException>(() => secret.DisposeOrThrow(TimeSpan.Zero));
            Assert.Throws<InvalidOperationException>(() => secret.Dispose());
        });
    }

    [Theory]
    [InlineData(ReturnKind.Task)]
    [InlineData(ReturnKind.GenericTask)]
    [InlineData(ReturnKind.ValueTask)]
    [InlineData(ReturnKind.GenericValueTask)]
    [InlineData(ReturnKind.IntPtr)]
    public void TransformSnapshot_RejectsUnsupportedReturnTypesBeforeRunningCallback(ReturnKind kind)
    {
        var api = new RecordingProtectedMemoryApi();
        using var secret = new SecureSecret(16, data => data.Fill(0x5C), api, TimeSpan.FromSeconds(1));

        Assert.Throws<NotSupportedException>(() => InvokeUnsupportedReturn(secret, kind));
        Assert.Equal(0, api.UnprotectCalls);
        Assert.Equal(1, api.ProtectCalls);
    }

    [Fact]
    public void SnapshotCleanup_ZeroesAndFreesSnapshotAfterCallback()
    {
        var api = new RecordingProtectedMemoryApi();
        using var secret = new SecureSecret(16, data => data.Fill(0x7A), api, TimeSpan.FromSeconds(1));

        secret.UseSnapshot(data => Assert.Equal(16, data.Length));

        Assert.Single(api.FreedBlocks);
        byte[] snapshotBytes = api.FreedBlocks[^1];
        Assert.Equal(16, snapshotBytes.Length);
        Assert.All(snapshotBytes, value => Assert.Equal(0, value));
        Assert.True(api.VirtualUnlockCalls >= 1);
    }

    [Fact]
    public void SnapshotCleanup_ZeroesAndFreesSnapshotWhenCallbackThrows()
    {
        var api = new RecordingProtectedMemoryApi();
        using var secret = new SecureSecret(16, data => data.Fill(0x7B), api, TimeSpan.FromSeconds(1));

        Assert.Throws<InvalidOperationException>(() => secret.UseSnapshot(_ => throw new InvalidOperationException("boom")));

        Assert.Single(api.FreedBlocks);
        byte[] snapshotBytes = api.FreedBlocks[^1];
        Assert.All(snapshotBytes, value => Assert.Equal(0, value));
    }

    [Fact]
    public void InterfaceReference_UsesSnapshotAccessWithoutOwningTransfer()
    {
        using var secret = CreateSecret(16, 0x6E);
        ISecureSecretSnapshotSource source = secret;

        byte[] snapshot = source.TransformSnapshot(data => data.ToArray());

        Assert.Equal(Enumerable.Repeat((byte)0x6E, 16), snapshot);
    }

    private static SecureSecret CreateSecret(int length, byte fillValue) =>
        new(length, data => data.Fill(fillValue), new RecordingProtectedMemoryApi(), TimeSpan.FromSeconds(1));

    private static object? InvokeUnsupportedReturn(SecureSecret secret, ReturnKind kind)
    {
        switch (kind)
        {
            case ReturnKind.Task:
                return secret.TransformSnapshot(_ => Task.CompletedTask);
            case ReturnKind.GenericTask:
                return secret.TransformSnapshot(_ => Task.FromResult(1));
            case ReturnKind.ValueTask:
                return secret.TransformSnapshot(_ => ValueTask.CompletedTask).AsTask();
            case ReturnKind.GenericValueTask:
                return secret.TransformSnapshot(_ => ValueTask.FromResult(1)).AsTask();
            case ReturnKind.IntPtr:
                return secret.TransformSnapshot(_ => IntPtr.Zero);
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

    [Fact]
    public void DisposeOrThrow_PropagatesCleanupFailure()
    {
        var api = new RecordingProtectedMemoryApi
        {
            ThrowOnFree = true
        };
        var secret = new SecureSecret(16, data => data.Fill(0x39), api, TimeSpan.FromSeconds(1));

        var ex = Assert.Throws<InvalidOperationException>(() => secret.DisposeOrThrow(TimeSpan.FromSeconds(1)));
        Assert.Contains("cleanup failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingProtectedMemoryApi : IProtectedMemoryApi
    {
        private readonly Dictionary<IntPtr, int> _allocations = [];
        private readonly object _allocationsGate = new();

        public int ProtectCalls { get; private set; }
        public int UnprotectCalls { get; private set; }
        public int VirtualLockCalls { get; private set; }
        public int VirtualUnlockCalls { get; private set; }
        public int FreeCalls { get; private set; }
        public bool FailVirtualLock { get; set; }
        public int? FailProtectOnCall { get; set; }
        public bool ThrowOnFree { get; set; }
        public byte[]? LastProtectedBytes { get; private set; }
        public byte[]? LastFreedBytes { get; private set; }
        public List<byte[]> FreedBlocks { get; } = [];
        public List<IntPtr> Allocations { get; } = [];
        public Action? BeforeCopy { get; set; }

        public IntPtr Allocate(int byteCount)
        {
            IntPtr address = Marshal.AllocHGlobal(byteCount);
            lock (_allocationsGate)
            {
                _allocations[address] = byteCount;
                Allocations.Add(address);
            }

            for (int i = 0; i < byteCount; i++)
                Marshal.WriteByte(address, i, 0xCC);

            return address;
        }

        public void Free(IntPtr address)
        {
            FreeCalls++;
            int byteCount;
            lock (_allocationsGate)
            {
                byteCount = _allocations[address];
                _allocations.Remove(address);
            }

            byte[] bytes = new byte[byteCount];
            Marshal.Copy(address, bytes, 0, byteCount);
            LastFreedBytes = bytes;
            FreedBlocks.Add(bytes);
            Marshal.FreeHGlobal(address);

            if (ThrowOnFree)
                throw new InvalidOperationException("Injected free failure.");
        }

        public bool VirtualLock(IntPtr address, int byteCount)
        {
            VirtualLockCalls++;
            return !FailVirtualLock;
        }

        public bool VirtualUnlock(IntPtr address, int byteCount)
        {
            VirtualUnlockCalls++;
            return true;
        }

        public bool CryptProtectMemory(IntPtr address, int byteCount)
        {
            ProtectCalls++;
            if (FailProtectOnCall == ProtectCalls)
                return false;

            byte[] bytes = new byte[byteCount];
            Marshal.Copy(address, bytes, 0, byteCount);
            LastProtectedBytes = bytes;
            return true;
        }

        public bool CryptUnprotectMemory(IntPtr address, int byteCount)
        {
            UnprotectCalls++;
            return true;
        }

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
