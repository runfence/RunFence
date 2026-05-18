using RunFence.Launch;

namespace RunFence.Launch.Container;

public interface IAppContainerProcessStarter
{
    ProcessLaunchNative.PROCESS_INFORMATION Start(
        IntPtr appContainerToken,
        ProcessLaunchTarget target,
        IntPtr environmentPointer);

    uint? GetImmediateExitCode(ProcessLaunchNative.PROCESS_INFORMATION processInformation);
}
