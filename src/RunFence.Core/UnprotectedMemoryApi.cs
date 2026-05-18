namespace RunFence.Core;

internal sealed class UnprotectedMemoryApi(IProtectedMemoryApi inner) : IProtectedMemoryApi
{
    public IntPtr Allocate(int byteCount) => inner.Allocate(byteCount);

    public void Free(IntPtr address) => inner.Free(address);

    public bool VirtualLock(IntPtr address, int byteCount) => true;

    public bool VirtualUnlock(IntPtr address, int byteCount) => true;

    public bool CryptProtectMemory(IntPtr address, int byteCount) => true;

    public bool CryptUnprotectMemory(IntPtr address, int byteCount) => true;

    public void ZeroMemory(IntPtr address, int byteCount) => inner.ZeroMemory(address, byteCount);

    public void CopyMemory(IntPtr source, IntPtr destination, int byteCount) => inner.CopyMemory(source, destination, byteCount);
}
