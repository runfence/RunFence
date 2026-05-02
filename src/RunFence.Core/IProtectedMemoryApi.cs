namespace RunFence.Core;

internal interface IProtectedMemoryApi
{
    IntPtr Allocate(int byteCount);
    void Free(IntPtr address);
    bool VirtualLock(IntPtr address, int byteCount);
    bool VirtualUnlock(IntPtr address, int byteCount);
    bool CryptProtectMemory(IntPtr address, int byteCount);
    bool CryptUnprotectMemory(IntPtr address, int byteCount);
    void ZeroMemory(IntPtr address, int byteCount);
    void CopyMemory(IntPtr source, IntPtr destination, int byteCount);
}
