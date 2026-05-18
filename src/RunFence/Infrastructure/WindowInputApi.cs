using System.Runtime.InteropServices;

namespace RunFence.Infrastructure;

public sealed class WindowInputApi : IWindowInputApi
{
    public SendInputCallResult SendInput(WindowNative.INPUT[] inputs)
    {
        uint sentCount = WindowNative.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<WindowNative.INPUT>());
        int lastError = sentCount == (uint)inputs.Length ? 0 : Marshal.GetLastWin32Error();
        return new SendInputCallResult(sentCount, lastError);
    }
}
