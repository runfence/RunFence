using System.Runtime.InteropServices;

namespace RunFence.AppxLauncher;

public sealed class HStringHandle(string value) : IDisposable
{
    public IntPtr Handle { get; } = Create(value);

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
            WinRtNative.WindowsDeleteString(Handle);
    }

    private static IntPtr Create(string value)
    {
        var hr = WinRtNative.WindowsCreateString(value, value.Length, out var handle);
        Marshal.ThrowExceptionForHR(hr);
        return handle;
    }
}
