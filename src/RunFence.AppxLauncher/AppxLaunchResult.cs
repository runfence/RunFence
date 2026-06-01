namespace RunFence.AppxLauncher;

public readonly record struct AppxLaunchResult(
    bool Success,
    AppxLaunchExitCode ExitCode,
    string Stage,
    int? HResult,
    string? Message)
{
    public static AppxLaunchResult Succeeded(string stage = "Completed", string? message = null) =>
        new(true, AppxLaunchExitCode.Success, stage, 0, message);

    public static AppxLaunchResult Failed(AppxLaunchExitCode exitCode, string stage, Exception ex) =>
        new(false, exitCode, stage, ex.HResult, ex.Message);

    public static AppxLaunchResult Failed(AppxLaunchExitCode exitCode, string stage, string message) =>
        new(false, exitCode, stage, null, message);
}
