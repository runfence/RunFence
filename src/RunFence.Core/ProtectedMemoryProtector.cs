using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RunFence.Core;

internal sealed class ProtectedMemoryProtector(IProtectedMemoryApi api, bool useProtection)
{
    public bool IsProtected { get; private set; }
    public bool IsVirtualLocked { get; private set; }

    public void Lock(IntPtr address, int byteCount)
    {
        if (!useProtection)
            return;

        IsVirtualLocked = api.VirtualLock(address, byteCount);
    }

    public void Protect(IntPtr address, int byteCount)
    {
        if (!useProtection || IsProtected)
            return;

        if (!api.CryptProtectMemory(address, byteCount))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        IsProtected = true;
    }

    public void Unprotect(IntPtr address, int byteCount)
    {
        if (!useProtection || !IsProtected)
            return;

        if (!api.CryptUnprotectMemory(address, byteCount))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        IsProtected = false;
    }

    public void ZeroBeforeRelease(IntPtr address, int byteCount)
    {
        if (address == IntPtr.Zero)
            return;

        if (useProtection && IsProtected)
            api.CryptUnprotectMemory(address, byteCount);

        api.ZeroMemory(address, byteCount);

        if (useProtection && IsVirtualLocked)
            api.VirtualUnlock(address, byteCount);

        IsProtected = false;
        IsVirtualLocked = false;
    }
}
