using System.Runtime.InteropServices;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class ProtectedStringNativeBufferTests
{
    [Fact]
    public void EnsureCapacity_GrowsAndPreservesBytes()
    {
        var api = new RecordingProtectedMemoryApi();
        using var buffer = new ProtectedStringNativeBuffer(api, useProtection: false);

        buffer.WithUnprotectedAccess(access =>
        {
            Marshal.WriteByte(access.Address, 0, 0x11);
            Marshal.WriteByte(access.Address, 15, 0x22);
        });

        buffer.EnsureCapacity(17);

        Assert.Equal(32, buffer.Capacity);
        buffer.WithUnprotectedAccess(access =>
        {
            Assert.Equal(0x11, Marshal.ReadByte(access.Address, 0));
            Assert.Equal(0x22, Marshal.ReadByte(access.Address, 15));
        });
    }

    [Fact]
    public void EnsureCapacity_WhenReplacementProtectFails_CleansReplacementAndKeepsOriginalBytes()
    {
        var api = new RecordingProtectedMemoryApi();
        using var buffer = new ProtectedStringNativeBuffer(api, useProtection: true);

        buffer.WithUnprotectedAccess(access => Marshal.WriteByte(access.Address, 0, 0x42));
        api.FailNextProtect = true;

        Assert.Throws<InvalidOperationException>(() => buffer.EnsureCapacity(17));

        Assert.NotNull(api.LastFreedBytes);
        Assert.Equal(32, api.LastFreedBytes!.Length);
        Assert.All(api.LastFreedBytes, value => Assert.Equal(0, value));

        buffer.WithUnprotectedAccess(access => Assert.Equal(0x42, Marshal.ReadByte(access.Address, 0)));
    }

    [Fact]
    public void WithUnprotectedAccess_WhenCallbackThrows_RestoresProtectionState()
    {
        var api = new RecordingProtectedMemoryApi();
        using var buffer = new ProtectedStringNativeBuffer(api, useProtection: true);

        Assert.Equal(1, api.ProtectCalls);

        Assert.Throws<InvalidOperationException>(() => buffer.WithUnprotectedAccess<int>(_ => throw new InvalidOperationException("boom")));

        Assert.Equal(1, api.UnprotectCalls);
        Assert.Equal(2, api.ProtectCalls);

        buffer.WithUnprotectedAccess(access => Marshal.WriteByte(access.Address, 0, 0x33));
        Assert.Equal(2, api.UnprotectCalls);
        Assert.Equal(3, api.ProtectCalls);
    }

    [Fact]
    public void WithUnprotectedAccess_WhenReprotectFails_DisposesBufferAndZerosBytes()
    {
        var api = new RecordingProtectedMemoryApi();
        using var buffer = new ProtectedStringNativeBuffer(api, useProtection: true);

        api.FailNextProtect = true;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            buffer.WithUnprotectedAccess(access => Marshal.WriteByte(access.Address, 0, 0x5A)));

        Assert.Contains("re-protection failed", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(api.LastFreedBytes);
        Assert.All(api.LastFreedBytes!, value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() =>
            buffer.WithUnprotectedAccess(_ => { }));
    }

    [Theory]
    [InlineData(1, 16)]
    [InlineData(16, 16)]
    [InlineData(17, 32)]
    [InlineData(31, 32)]
    [InlineData(32, 32)]
    public void RoundUpToBlockSize_RoundsAsExpected(int value, int expected)
    {
        Assert.Equal(expected, CryptMemoryNative.RoundUpToBlockSize(value));
    }

    private sealed class RecordingProtectedMemoryApi : IProtectedMemoryApi
    {
        private readonly Dictionary<IntPtr, int> _allocations = [];

        public int ProtectCalls { get; private set; }
        public int UnprotectCalls { get; private set; }
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
            int byteCount = _allocations[address];
            LastFreedBytes = new byte[byteCount];
            Marshal.Copy(address, LastFreedBytes, 0, byteCount);
            _allocations.Remove(address);
            Marshal.FreeHGlobal(address);
        }

        public bool VirtualLock(IntPtr address, int byteCount) => true;

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
