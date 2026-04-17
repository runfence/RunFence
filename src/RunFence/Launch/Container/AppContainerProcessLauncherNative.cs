using System.Runtime.InteropServices;
using System.Text;

namespace RunFence.Launch.Container;

public static class AppContainerProcessLauncherNative
{
    public const uint SE_GROUP_ENABLED = 0x00000004;

    [DllImport("kernelbase.dll", SetLastError = true)]
    public static extern bool CreateAppContainerToken(
        IntPtr TokenHandle,
        ref SECURITY_CAPABILITIES SecurityCapabilities,
        out IntPtr OutToken);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool GetAppContainerNamedObjectPath(
        IntPtr Token, IntPtr AppContainerSid, uint ObjectPathLength,
        StringBuilder ObjectPath, out uint ReturnLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_CAPABILITIES
    {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
    }
}
