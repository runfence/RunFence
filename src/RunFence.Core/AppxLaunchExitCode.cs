namespace RunFence.Core;

public enum AppxLaunchExitCode
{
    Success = 0,
    InvalidArguments = 1,
    RoInitializeFailed = 2,
    CreateUriFailed = 3,
    CreateLauncherOptionsFailed = 4,
    SetTargetPackageFamilyFailed = 5,
    LaunchUriAsyncFailed = 6,
    ResultFileWriteFailed = 7,
    AsyncObservationFailed = 8,
    ManifestResolutionFailed = 9,
    DesktopAppxActivationFailed = 10,
    ProtocolResolutionFailed = 11,
    ShellExecuteFailed = 12,
    TargetProcessVerificationFailed = 13
}
