using RunFence.Launching.Environment;

namespace RunFence.Launching.Resolution;

public sealed record ExecutablePathResolutionContext(
    IEnvironmentVariableReader? EnvironmentReader,
    string? TargetUserSid,
    bool SearchCurrentProcessPath)
{
    public static ExecutablePathResolutionContext CurrentProcess(string? targetUserSid = null) =>
        new(null, targetUserSid, SearchCurrentProcessPath: true);

    public static ExecutablePathResolutionContext TargetEnvironment(
        IEnvironmentVariableReader environmentReader,
        string? targetUserSid = null) =>
        new(environmentReader, targetUserSid, SearchCurrentProcessPath: false);

    public static ExecutablePathResolutionContext DirectOnly() =>
        new(null, TargetUserSid: null, SearchCurrentProcessPath: false);
}
