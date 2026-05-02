namespace RunFence.Launch.Tokens;

public interface IRestrictedJobLaunchCoordinator
{
    ProcessLaunchNative.PROCESS_INFORMATION SeedJobKeeperAndLaunch(
        IntPtr hToken,
        LaunchTokenSource tokenSource,
        string sid,
        bool isLow,
        ProcessLaunchTarget psi);

    ProcessInfo LaunchViaJobKeeper(string sid, bool isLow, ProcessLaunchTarget psi);
}
