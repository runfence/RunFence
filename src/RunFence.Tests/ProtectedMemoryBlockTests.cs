using System.Runtime.InteropServices;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class ProtectedMemoryBlockTests
{
    [Fact]
    public void EnsureCapacity_GrowsAndPreservesBytes()
    {
        var api = new RecordingProtectedMemoryApi();
        using var block = new ProtectedMemoryBlock(16, protect: false, api);

        using (var scope = block.Unprotect())
        {
            Marshal.WriteByte(scope.Address, 0, 0x11);
            Marshal.WriteByte(scope.Address, 15, 0x22);
        }

        block.EnsureCapacity(32);

        Assert.Equal(32, block.Capacity);
        using var grown = block.Unprotect();
        Assert.Equal(0x11, Marshal.ReadByte(grown.Address, 0));
        Assert.Equal(0x22, Marshal.ReadByte(grown.Address, 15));
    }

    [Fact]
    public void Dispose_ZeroesMemoryBeforeFree()
    {
        var api = new RecordingProtectedMemoryApi();
        var block = new ProtectedMemoryBlock(16, protect: false, api);

        using (var scope = block.Unprotect())
            Marshal.WriteByte(scope.Address, 0, 0x7F);

        block.Dispose();

        Assert.NotNull(api.LastFreedBytes);
        Assert.All(api.LastFreedBytes!, b => Assert.Equal(0, b));
    }

    [Fact]
    public void ProtectUnprotect_TracksStateTransitions()
    {
        var api = new RecordingProtectedMemoryApi();
        using var block = new ProtectedMemoryBlock(16, protect: true, api);

        Assert.True(block.IsProtected);
        Assert.Equal(1, api.ProtectCalls);

        var scope = block.Unprotect();
        Assert.False(block.IsProtected);
        Assert.Equal(1, api.UnprotectCalls);

        scope.Dispose();

        Assert.True(block.IsProtected);
        Assert.Equal(2, api.ProtectCalls);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var api = new RecordingProtectedMemoryApi();
        var block = new ProtectedMemoryBlock(16, protect: false, api);

        block.Dispose();
        block.Dispose();

        Assert.Equal(1, api.FreeCalls);
    }

    [Fact]
    public void ProtectFalse_SkipsNativeProtectionAndVirtualLock()
    {
        var api = new RecordingProtectedMemoryApi();
        using var block = new ProtectedMemoryBlock(16, protect: false, api);

        Assert.False(block.IsProtected);
        Assert.Equal(0, api.VirtualLockCalls);
        Assert.Equal(0, api.ProtectCalls);

        using var scope = block.Unprotect();
        Assert.NotEqual(IntPtr.Zero, scope.Address);
        Assert.Equal(0, api.UnprotectCalls);
    }

    [Fact]
    public void EnsureCapacity_WhenNewProtectionFails_ReprotectsOriginalBlock()
    {
        var api = new RecordingProtectedMemoryApi();
        using var block = new ProtectedMemoryBlock(16, protect: true, api);

        using (var scope = block.Unprotect())
            Marshal.WriteByte(scope.Address, 0, 0x42);

        api.FailNextProtect = true;

        Assert.Throws<InvalidOperationException>(() => block.EnsureCapacity(32));

        Assert.True(block.IsProtected);
        using var restored = block.Unprotect();
        Assert.Equal(0x42, Marshal.ReadByte(restored.Address, 0));
    }

    [Fact]
    public void Clear_ZeroesWholeBlock()
    {
        var api = new RecordingProtectedMemoryApi();
        using var block = new ProtectedMemoryBlock(16, protect: false, api);

        using (var scope = block.Unprotect())
        {
            Marshal.WriteByte(scope.Address, 0, 0x11);
            Marshal.WriteByte(scope.Address, 15, 0x22);
        }

        block.Clear();

        using var cleared = block.Unprotect();
        for (int i = 0; i < block.Capacity; i++)
        {
            Assert.Equal(0, Marshal.ReadByte(cleared.Address, i));
        }
    }

    [Fact]
    public void Clear_LeavesBlockProtected()
    {
        var api = new RecordingProtectedMemoryApi();
        using var block = new ProtectedMemoryBlock(16, protect: true, api);

        block.Clear();

        Assert.True(block.IsProtected);
    }

    [Fact]
    public void Clear_RejectsActiveScope()
    {
        var api = new RecordingProtectedMemoryApi();
        using var block = new ProtectedMemoryBlock(16, protect: false, api);

        using var scope = block.Unprotect();

        Assert.Throws<InvalidOperationException>(() => block.Clear());
    }

    [Fact]
    public void DefaultScopeDispose_DoesNotThrow()
    {
        var scope = default(ProtectedMemoryBlock.UnprotectScope);

        scope.Dispose();
    }

    [Fact]
    public void DefaultScopeAddress_ThrowsInvalidOperation()
    {
        var scope = default(ProtectedMemoryBlock.UnprotectScope);

        var threw = false;
        try
        {
            _ = scope.Address;
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assert.True(threw, "Expected default UnprotectScope address access to fail clearly.");
    }

    private sealed class RecordingProtectedMemoryApi : IProtectedMemoryApi
    {
        private readonly Dictionary<IntPtr, int> _allocations = new();

        public int ProtectCalls { get; private set; }
        public int UnprotectCalls { get; private set; }
        public int VirtualLockCalls { get; private set; }
        public int FreeCalls { get; private set; }
        public byte[]? LastFreedBytes { get; private set; }
        public bool FailNextProtect { get; set; }

        public IntPtr Allocate(int byteCount)
        {
            IntPtr address = Marshal.AllocHGlobal(byteCount);
            _allocations[address] = byteCount;
            return address;
        }

        public void Free(IntPtr address)
        {
            FreeCalls++;
            int byteCount = _allocations[address];
            LastFreedBytes = new byte[byteCount];
            Marshal.Copy(address, LastFreedBytes, 0, byteCount);
            _allocations.Remove(address);
            Marshal.FreeHGlobal(address);
        }

        public bool VirtualLock(IntPtr address, int byteCount)
        {
            VirtualLockCalls++;
            return true;
        }

        public bool VirtualUnlock(IntPtr address, int byteCount) => true;

        public bool CryptProtectMemory(IntPtr address, int byteCount)
        {
            if (FailNextProtect)
            {
                FailNextProtect = false;
                throw new InvalidOperationException("Protect failed.");
            }

            ProtectCalls++;
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
            byte[] bytes = new byte[byteCount];
            Marshal.Copy(source, bytes, 0, byteCount);
            Marshal.Copy(bytes, 0, destination, byteCount);
        }
    }
}
