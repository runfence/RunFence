namespace RunFence.AppxLauncher;

public sealed class AppxLaunchOrchestrator(
    IAppxManifestLaunchMetadataResolver metadataResolver,
    IDesktopAppxActivationLauncher desktopActivationLauncher,
    IAppxPackageRegistration packageRegistration,
    IAppxUriProtocolLauncher uriProtocolLauncher,
    IShellUriProtocolLauncher shellUriProtocolLauncher,
    IAppxLaunchAttemptVerifier launchAttemptVerifier)
{
    public AppxLaunchResult Launch(AppxLauncherStartupOptions options)
    {
        var metadataResult = metadataResolver.Resolve(options.AppxExecutablePath, options.Arguments);
        if (!metadataResult.Success)
            return metadataResult.Error;

        return metadataResult.Metadata.IsFullTrustApplication
            ? LaunchFullTrust(metadataResult.Metadata, options.Arguments)
            : LaunchUriWithRegistrationRetry(metadataResult.Metadata, options.Arguments);
    }

    private AppxLaunchResult LaunchFullTrust(AppxManifestLaunchMetadata metadata, string arguments)
    {
        var initialLaunchResult = launchAttemptVerifier.Verify(
            metadata,
            AppxLaunchVerificationKind.FullTrustActivation,
            () => desktopActivationLauncher.Launch(metadata, arguments));
        if (initialLaunchResult.Success)
            return initialLaunchResult;

        try
        {
            packageRegistration.RegisterPackageByFamilyName(metadata.PackageFamilyName);
        }
        catch
        {
            return initialLaunchResult;
        }

        var retryLaunchResult = launchAttemptVerifier.Verify(
            metadata,
            AppxLaunchVerificationKind.FullTrustActivation,
            () => desktopActivationLauncher.Launch(metadata, arguments));
        if (retryLaunchResult.Success)
            return retryLaunchResult;

        return LaunchUriThenShell(metadata, arguments, retryLaunchResult);
    }

    private AppxLaunchResult LaunchUriWithRegistrationRetry(AppxManifestLaunchMetadata metadata, string arguments)
    {
        var fallbackUri = BuildFallbackUri(metadata.Protocol, arguments);
        if (fallbackUri == null)
        {
            return AppxLaunchResult.Failed(
                AppxLaunchExitCode.ProtocolResolutionFailed,
                "ResolveProtocol",
                "AppX manifest does not define a protocol for URI activation.");
        }

        var initialLaunchResult = launchAttemptVerifier.Verify(
            metadata,
            AppxLaunchVerificationKind.UriActivation,
            () => uriProtocolLauncher.Launch(new AppxUriLaunchOptions(metadata.PackageFamilyName, fallbackUri)));
        if (initialLaunchResult.Success)
            return initialLaunchResult;

        try
        {
            packageRegistration.RegisterPackageByFamilyName(metadata.PackageFamilyName);
        }
        catch
        {
            return initialLaunchResult;
        }

        var retryLaunchResult = launchAttemptVerifier.Verify(
            metadata,
            AppxLaunchVerificationKind.UriActivation,
            () => uriProtocolLauncher.Launch(new AppxUriLaunchOptions(metadata.PackageFamilyName, fallbackUri)));
        if (retryLaunchResult.Success)
            return retryLaunchResult;

        var shellLaunchResult = launchAttemptVerifier.Verify(
            metadata,
            AppxLaunchVerificationKind.UriActivation,
            () => shellUriProtocolLauncher.Launch(fallbackUri));
        return shellLaunchResult.Success ? shellLaunchResult : retryLaunchResult;
    }

    private AppxLaunchResult LaunchUriThenShell(
        AppxManifestLaunchMetadata metadata,
        string arguments,
        AppxLaunchResult primaryFailure)
    {
        var fallbackUri = BuildFallbackUri(metadata.Protocol, arguments);
        if (fallbackUri == null)
            return primaryFailure;

        var fallbackResult = launchAttemptVerifier.Verify(
            metadata,
            AppxLaunchVerificationKind.UriActivation,
            () => uriProtocolLauncher.Launch(new AppxUriLaunchOptions(metadata.PackageFamilyName, fallbackUri)));
        if (fallbackResult.Success)
            return fallbackResult;

        var shellLaunchResult = launchAttemptVerifier.Verify(
            metadata,
            AppxLaunchVerificationKind.UriActivation,
            () => shellUriProtocolLauncher.Launch(fallbackUri));
        return shellLaunchResult.Success ? shellLaunchResult : primaryFailure;
    }

    private static string? BuildFallbackUri(string? protocol, string arguments)
    {
        if (string.IsNullOrWhiteSpace(protocol))
            return null;

        var trimmedArguments = arguments.TrimStart();
        var prefix = protocol + ":";
        return trimmedArguments.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmedArguments
            : prefix + arguments;
    }
}
