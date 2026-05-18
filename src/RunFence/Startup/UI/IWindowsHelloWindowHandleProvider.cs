using System;

namespace RunFence.Startup.UI;

public interface IWindowsHelloWindowHandleProvider
{
    IntPtr GetForegroundWindowHandle();
}
