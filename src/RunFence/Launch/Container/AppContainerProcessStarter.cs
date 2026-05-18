using System.ComponentModel;
using RunFence.Core;

namespace RunFence.Launch.Container;

public class AppContainerProcessStarter(ILoggingService log) : IAppContainerProcessStarter
{
    public ProcessLaunchNative.PROCESS_INFORMATION Start(
        IntPtr appContainerToken,
        ProcessLaunchTarget target,
        IntPtr environmentPointer)
    {
        try
        {
            return ProcessLaunchNative.CreateProcessWithToken(appContainerToken, target, environmentPointer, log);
        }
        catch (Win32Exception ex)
        {
            log.Error($"AppContainerProcessLauncher: CreateProcessWithTokenW failed - Win32 error {ex.NativeErrorCode} (0x{ex.NativeErrorCode:X8}): {ex.Message}");
            throw;
        }
    }

    public uint? GetImmediateExitCode(ProcessLaunchNative.PROCESS_INFORMATION processInformation)
    {
        if (ProcessLaunchNative.WaitForSingleObject(processInformation.hProcess, 100) != ProcessLaunchNative.WAIT_OBJECT_0)
            return null;
        if (!ProcessLaunchNative.GetExitCodeProcess(processInformation.hProcess, out var exitCode))
            return null;
        if (exitCode == 0)
            return 0;

        var hint = exitCode switch
        {
            0xC0000135 => " (DLL not found - the application may depend on DLLs not accessible to the container)",
            0xC0000142 => " (DLL initialization failed - often user32.dll; may indicate missing desktop/winstation access)",
            0xC0000022 => " (Access denied - the application may need additional file or registry permissions)",
            _ => ""
        };

        log.Error($"AppContainerProcessLauncher: Process exited immediately with code 0x{exitCode:X8}{hint}");
        throw new InvalidOperationException($"Process exited immediately (code 0x{exitCode:X8}).{hint}");
    }
}
