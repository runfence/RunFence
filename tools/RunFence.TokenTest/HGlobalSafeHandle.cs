using System.Runtime.InteropServices;

namespace RunFence.TokenTest;

internal sealed class HGlobalSafeHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
{
    public HGlobalSafeHandle(IntPtr ptr) : base(true) { SetHandle(ptr); }
    protected override bool ReleaseHandle() { Marshal.FreeHGlobal(handle); return true; }
}
