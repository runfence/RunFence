using System.Runtime.InteropServices;

namespace RunFence.Infrastructure;

[StructLayout(LayoutKind.Sequential)]
public struct WindowRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
