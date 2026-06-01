using System.Runtime.InteropServices;

namespace RunFence.AppxLauncher;

[ComImport]
[Guid("F158268A-D5A5-45CE-99CF-00D6C3F3FC0A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDesktopAppXActivator
{
    void Activate(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string command,
        [MarshalAs(UnmanagedType.LPWStr)] string args,
        out uint processId);

    void ActivateWithOptions(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string command,
        [MarshalAs(UnmanagedType.LPWStr)] string args,
        uint options,
        uint reserved,
        out uint processId);

    void ActivateWithOptionsAndArgs(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string command,
        [MarshalAs(UnmanagedType.LPWStr)] string args,
        uint options,
        uint reserved,
        [MarshalAs(UnmanagedType.Struct)] object additionalArgs,
        out uint processId);

    void ActivateWithOptionsArgsWorkingDirectoryShowWindow(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string command,
        [MarshalAs(UnmanagedType.LPWStr)] string args,
        uint options,
        uint reserved,
        [MarshalAs(UnmanagedType.Struct)] object additionalArgs,
        [MarshalAs(UnmanagedType.LPWStr)] string workingDirectory,
        uint showWindow,
        out uint processId);
}
