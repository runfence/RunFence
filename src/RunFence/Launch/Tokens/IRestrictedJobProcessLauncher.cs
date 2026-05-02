using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

public interface IRestrictedJobProcessLauncher
{
    ProcessLaunchNative.PROCESS_INFORMATION LaunchWithPreparedToken(
        IntPtr token,
        ProcessLaunchTarget target,
        LaunchTokenSource tokenSource,
        string accountSid,
        bool allowUnsuspendedRetry = true);
}
