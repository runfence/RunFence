using System.Runtime.InteropServices;

namespace RunFence.Core;

internal sealed class NativeProtectedMemoryApi : IProtectedMemoryApi
{
    public static readonly NativeProtectedMemoryApi Instance = new();

    private NativeProtectedMemoryApi()
    {
    }

    public IntPtr Allocate(int byteCount) => Marshal.AllocHGlobal(byteCount);

    public void Free(IntPtr address) => Marshal.FreeHGlobal(address);

    public bool VirtualLock(IntPtr address, int byteCount) =>
        CryptMemoryNative.VirtualLock(address, (UIntPtr)byteCount);

    public bool VirtualUnlock(IntPtr address, int byteCount) =>
        CryptMemoryNative.VirtualUnlock(address, (UIntPtr)byteCount);

    public bool CryptProtectMemory(IntPtr address, int byteCount) =>
        CryptMemoryNative.CryptProtectMemory(address, (uint)byteCount, CryptMemoryNative.CRYPTPROTECTMEMORY_SAME_PROCESS);

    public bool CryptUnprotectMemory(IntPtr address, int byteCount) =>
        CryptMemoryNative.CryptUnprotectMemory(address, (uint)byteCount, CryptMemoryNative.CRYPTPROTECTMEMORY_SAME_PROCESS);

    public void ZeroMemory(IntPtr address, int byteCount)
    {
        for (int i = 0; i < byteCount; i++)
            Marshal.WriteByte(address, i, 0);
    }

    public void CopyMemory(IntPtr source, IntPtr destination, int byteCount)
    {
        unsafe
        {
            Buffer.MemoryCopy(source.ToPointer(), destination.ToPointer(), byteCount, byteCount);
        }
    }
}
