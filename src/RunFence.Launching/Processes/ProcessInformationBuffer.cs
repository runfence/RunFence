using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RunFence.Launching.Processes;

public sealed class ProcessInformationBuffer : SafeHandleZeroOrMinusOneIsInvalid
{
    public ProcessInformationBuffer(int byteLength)
        : base(true)
    {
        SetHandle(Marshal.AllocHGlobal(byteLength));
    }

    protected override bool ReleaseHandle()
    {
        Marshal.FreeHGlobal(handle);
        return true;
    }
}
