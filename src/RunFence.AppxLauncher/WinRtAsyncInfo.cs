using System.Runtime.InteropServices;

namespace RunFence.AppxLauncher;

public sealed class WinRtAsyncInfo(IntPtr asyncOperation) : IDisposable
{
    private static readonly Guid AsyncInfoIid = new("00000036-0000-0000-C000-000000000046");
    private const int AsyncStatusSlot = 7;
    private const int AsyncErrorCodeSlot = 8;

    private IntPtr _asyncInfo = QueryInterface(asyncOperation, AsyncInfoIid);

    public AsyncStatus GetStatus()
    {
        var method = GetMethod<GetAsyncStatusDelegate>(_asyncInfo, AsyncStatusSlot);
        var hr = method(_asyncInfo, out AsyncStatus status);
        Marshal.ThrowExceptionForHR(hr);
        return status;
    }

    public int GetErrorCode()
    {
        var method = GetMethod<GetAsyncErrorCodeDelegate>(_asyncInfo, AsyncErrorCodeSlot);
        var hr = method(_asyncInfo, out var errorCode);
        Marshal.ThrowExceptionForHR(hr);
        return errorCode;
    }

    public void Dispose()
    {
        if (_asyncInfo == IntPtr.Zero)
            return;

        Marshal.Release(_asyncInfo);
        _asyncInfo = IntPtr.Zero;
    }

    private static IntPtr QueryInterface(IntPtr instance, Guid iid)
    {
        var hr = Marshal.QueryInterface(instance, in iid, out var interfacePtr);
        Marshal.ThrowExceptionForHR(hr);
        return interfacePtr;
    }

    private static T GetMethod<T>(IntPtr instance, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var methodPtr = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(methodPtr);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetAsyncStatusDelegate(IntPtr asyncInfo, out AsyncStatus status);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetAsyncErrorCodeDelegate(IntPtr asyncInfo, out int errorCode);
}
