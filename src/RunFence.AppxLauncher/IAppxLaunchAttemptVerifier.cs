namespace RunFence.AppxLauncher;

public interface IAppxLaunchAttemptVerifier
{
    AppxLaunchResult Verify(
        AppxManifestLaunchMetadata metadata,
        AppxLaunchVerificationKind verificationKind,
        Func<AppxLaunchResult> launch);
}
