using RunFence.AppxLauncher;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public sealed class AppxLaunchOrchestratorTests
{
    private static readonly AppxLauncherStartupOptions Options =
        new(
            "log.jsonl",
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.527.3686.0_x64__2p2nqsd0c76g0\app\Codex.exe",
            "codex:--resume abc");

    private static readonly AppxManifestLaunchMetadata FullTrustMetadata =
        new(
            "OpenAI.Codex_2p2nqsd0c76g0",
            "OpenAI.Codex_2p2nqsd0c76g0!App",
            "app/Codex.exe",
            "codex",
            true);

    private static readonly AppxManifestLaunchMetadata UriOnlyMetadata = FullTrustMetadata with
    {
        IsFullTrustApplication = false
    };

    [Fact]
    public void Launch_MetadataResolutionFails_ReturnsMetadataError()
    {
        var metadataError = AppxLaunchResult.Failed(
            AppxLaunchExitCode.ManifestResolutionFailed,
            "ResolvePackagePath",
            "Invalid package path.");
        var desktopLauncher = new FakeDesktopActivationLauncher();
        var packageRegistration = new FakePackageRegistration();
        var uriProtocolLauncher = new FakeUriProtocolLauncher();
        var shellLauncher = new FakeShellUriProtocolLauncher();
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Failed(metadataError),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.Equal(metadataError, result);
        Assert.Equal(0, desktopLauncher.CallCount);
        Assert.Equal(0, packageRegistration.CallCount);
        Assert.Equal(0, uriProtocolLauncher.CallCount);
        Assert.Equal(0, shellLauncher.CallCount);
    }

    [Fact]
    public void Launch_FullTrustFirstDesktopActivationSucceeds_DoesNotRegisterOrUseUriFallback()
    {
        var success = AppxLaunchResult.Succeeded("DesktopAppxActivateWithOptions");
        var desktopLauncher = new FakeDesktopActivationLauncher(success);
        var packageRegistration = new FakePackageRegistration();
        var uriProtocolLauncher = new FakeUriProtocolLauncher();
        var shellLauncher = new FakeShellUriProtocolLauncher();
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(FullTrustMetadata),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.Equal(success, result);
        Assert.Equal(1, desktopLauncher.CallCount);
        Assert.Equal(Options.Arguments, desktopLauncher.LastArguments);
        Assert.Equal(FullTrustMetadata, desktopLauncher.LastMetadata);
        Assert.Equal(0, packageRegistration.CallCount);
        Assert.Equal(0, uriProtocolLauncher.CallCount);
        Assert.Equal(0, shellLauncher.CallCount);
    }

    [Fact]
    public void Launch_FullTrustFirstDesktopActivationFailsAndRegistrationFails_ReturnsOriginalDesktopError()
    {
        var originalFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.DesktopAppxActivationFailed,
            "DesktopAppxActivateWithOptions",
            "Initial launch failed.");
        var desktopLauncher = new FakeDesktopActivationLauncher(originalFailure);
        var packageRegistration = new FakePackageRegistration(new InvalidOperationException("Registration failed."));
        var uriProtocolLauncher = new FakeUriProtocolLauncher();
        var shellLauncher = new FakeShellUriProtocolLauncher();
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(FullTrustMetadata),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.Equal(originalFailure, result);
        Assert.Equal(1, desktopLauncher.CallCount);
        Assert.Equal(1, packageRegistration.CallCount);
        Assert.Equal(FullTrustMetadata.PackageFamilyName, packageRegistration.LastPackageFamilyName);
        Assert.Equal(0, uriProtocolLauncher.CallCount);
        Assert.Equal(0, shellLauncher.CallCount);
    }

    [Fact]
    public void Launch_FullTrustRegistrationSucceeds_SecondDesktopActivationSucceeds()
    {
        var originalFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.DesktopAppxActivationFailed,
            "DesktopAppxActivateWithOptions",
            "Initial launch failed.");
        var retrySuccess = AppxLaunchResult.Succeeded("DesktopAppxActivateWithOptions");
        var desktopLauncher = new FakeDesktopActivationLauncher(originalFailure, retrySuccess);
        var packageRegistration = new FakePackageRegistration();
        var uriProtocolLauncher = new FakeUriProtocolLauncher();
        var shellLauncher = new FakeShellUriProtocolLauncher();
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(FullTrustMetadata),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.Equal(retrySuccess, result);
        Assert.Equal(2, desktopLauncher.CallCount);
        Assert.Equal(1, packageRegistration.CallCount);
        Assert.Equal(0, uriProtocolLauncher.CallCount);
        Assert.Equal(0, shellLauncher.CallCount);
    }

    [Fact]
    public void Launch_FullTrustSecondDesktopActivationFailsAndUriFallbackSucceeds_ReturnsUriSuccess()
    {
        var originalFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.DesktopAppxActivationFailed,
            "DesktopAppxActivateWithOptions",
            "Initial launch failed.");
        var retryFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.DesktopAppxActivationFailed,
            "DesktopAppxActivateWithOptions",
            "Retry failed.");
        var fallbackSuccess = AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted");
        var desktopLauncher = new FakeDesktopActivationLauncher(originalFailure, retryFailure);
        var packageRegistration = new FakePackageRegistration();
        var uriProtocolLauncher = new FakeUriProtocolLauncher(fallbackSuccess);
        var shellLauncher = new FakeShellUriProtocolLauncher();
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(FullTrustMetadata),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.Equal(fallbackSuccess, result);
        Assert.Equal(2, desktopLauncher.CallCount);
        Assert.Equal(1, packageRegistration.CallCount);
        Assert.Equal(1, uriProtocolLauncher.CallCount);
        Assert.Equal(0, shellLauncher.CallCount);
        Assert.Equal(new AppxUriLaunchOptions(FullTrustMetadata.PackageFamilyName, Options.Arguments), uriProtocolLauncher.LastOptions);
    }

    [Fact]
    public void Launch_FullTrustUriFallbackFailsAndShellExecuteSucceeds_ReturnsShellSuccess()
    {
        var retryFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.DesktopAppxActivationFailed,
            "DesktopAppxActivateWithOptions",
            "Retry failed.");
        var shellSuccess = AppxLaunchResult.Succeeded("ShellExecute");
        var desktopLauncher = new FakeDesktopActivationLauncher(retryFailure, retryFailure);
        var packageRegistration = new FakePackageRegistration();
        var uriProtocolLauncher = new FakeUriProtocolLauncher(AppxLaunchResult.Failed(
            AppxLaunchExitCode.LaunchUriAsyncFailed,
            "LaunchUriAsync",
            "URI fallback failed."));
        var shellLauncher = new FakeShellUriProtocolLauncher(shellSuccess);
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(FullTrustMetadata),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.Equal(shellSuccess, result);
        Assert.Equal(1, shellLauncher.CallCount);
        Assert.Equal(Options.Arguments, shellLauncher.LastUri);
    }

    [Fact]
    public void Launch_FullTrustUriFallbackAndShellExecuteFail_ReturnsSecondDesktopError()
    {
        var originalFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.DesktopAppxActivationFailed,
            "DesktopAppxActivateWithOptions",
            "Initial launch failed.");
        var retryFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.DesktopAppxActivationFailed,
            "DesktopAppxActivateWithOptions",
            "Retry failed.");
        var desktopLauncher = new FakeDesktopActivationLauncher(originalFailure, retryFailure);
        var packageRegistration = new FakePackageRegistration();
        var uriProtocolLauncher = new FakeUriProtocolLauncher(AppxLaunchResult.Failed(
            AppxLaunchExitCode.LaunchUriAsyncFailed,
            "LaunchUriAsync",
            "URI fallback failed."));
        var shellLauncher = new FakeShellUriProtocolLauncher(AppxLaunchResult.Failed(
            AppxLaunchExitCode.ShellExecuteFailed,
            "ShellExecute",
            "Shell fallback failed."));
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(FullTrustMetadata),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.Equal(retryFailure, result);
        Assert.Equal(2, desktopLauncher.CallCount);
        Assert.Equal(1, packageRegistration.CallCount);
        Assert.Equal(1, uriProtocolLauncher.CallCount);
        Assert.Equal(1, shellLauncher.CallCount);
    }

    [Fact]
    public void Launch_FullTrustSecondDesktopActivationFailsWithoutProtocol_ReturnsSecondDesktopErrorWithoutUriFallback()
    {
        var metadata = FullTrustMetadata with { Protocol = null };
        var originalFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.DesktopAppxActivationFailed,
            "DesktopAppxActivateWithOptions",
            "Initial launch failed.");
        var retryFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.DesktopAppxActivationFailed,
            "DesktopAppxActivateWithOptions",
            "Retry failed.");
        var desktopLauncher = new FakeDesktopActivationLauncher(originalFailure, retryFailure);
        var packageRegistration = new FakePackageRegistration();
        var uriProtocolLauncher = new FakeUriProtocolLauncher();
        var shellLauncher = new FakeShellUriProtocolLauncher();
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(metadata),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.Equal(retryFailure, result);
        Assert.Equal(2, desktopLauncher.CallCount);
        Assert.Equal(1, packageRegistration.CallCount);
        Assert.Equal(0, uriProtocolLauncher.CallCount);
        Assert.Equal(0, shellLauncher.CallCount);
    }

    [Fact]
    public void Launch_NonFullTrustInitialUriLaunchSucceeds_SkipsDesktopActivationAndRegistration()
    {
        var uriSuccess = AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted");
        var desktopLauncher = new FakeDesktopActivationLauncher();
        var packageRegistration = new FakePackageRegistration();
        var uriProtocolLauncher = new FakeUriProtocolLauncher(uriSuccess);
        var shellLauncher = new FakeShellUriProtocolLauncher();
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(UriOnlyMetadata),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.Equal(uriSuccess, result);
        Assert.Equal(0, desktopLauncher.CallCount);
        Assert.Equal(0, packageRegistration.CallCount);
        Assert.Equal(1, uriProtocolLauncher.CallCount);
        Assert.Equal(0, shellLauncher.CallCount);
    }

    [Fact]
    public void Launch_NonFullTrustInitialUriLaunchFailsAndRegistrationFails_ReturnsInitialUriError()
    {
        var uriFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.LaunchUriAsyncFailed,
            "LaunchUriAsync",
            "URI launch failed.");
        var desktopLauncher = new FakeDesktopActivationLauncher();
        var packageRegistration = new FakePackageRegistration(new InvalidOperationException("Registration failed."));
        var uriProtocolLauncher = new FakeUriProtocolLauncher(uriFailure);
        var shellLauncher = new FakeShellUriProtocolLauncher();
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(UriOnlyMetadata),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.Equal(uriFailure, result);
        Assert.Equal(0, desktopLauncher.CallCount);
        Assert.Equal(1, packageRegistration.CallCount);
        Assert.Equal(1, uriProtocolLauncher.CallCount);
        Assert.Equal(0, shellLauncher.CallCount);
    }

    [Fact]
    public void Launch_NonFullTrustRegistrationSucceedsAndUriRetrySucceeds_ReturnsRetrySuccess()
    {
        var uriFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.LaunchUriAsyncFailed,
            "LaunchUriAsync",
            "URI launch failed.");
        var retrySuccess = AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted");
        var desktopLauncher = new FakeDesktopActivationLauncher();
        var packageRegistration = new FakePackageRegistration();
        var uriProtocolLauncher = new FakeUriProtocolLauncher(uriFailure, retrySuccess);
        var shellLauncher = new FakeShellUriProtocolLauncher();
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(UriOnlyMetadata),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.Equal(retrySuccess, result);
        Assert.Equal(0, desktopLauncher.CallCount);
        Assert.Equal(1, packageRegistration.CallCount);
        Assert.Equal(2, uriProtocolLauncher.CallCount);
        Assert.Equal(0, shellLauncher.CallCount);
    }

    [Fact]
    public void Launch_NonFullTrustUriRetryFailsAndShellExecuteSucceeds_ReturnsShellSuccess()
    {
        var uriFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.LaunchUriAsyncFailed,
            "LaunchUriAsync",
            "URI launch failed.");
        var shellSuccess = AppxLaunchResult.Succeeded("ShellExecute");
        var desktopLauncher = new FakeDesktopActivationLauncher();
        var packageRegistration = new FakePackageRegistration();
        var uriProtocolLauncher = new FakeUriProtocolLauncher(uriFailure, uriFailure);
        var shellLauncher = new FakeShellUriProtocolLauncher(shellSuccess);
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(UriOnlyMetadata),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.Equal(shellSuccess, result);
        Assert.Equal(0, desktopLauncher.CallCount);
        Assert.Equal(1, packageRegistration.CallCount);
        Assert.Equal(2, uriProtocolLauncher.CallCount);
        Assert.Equal(1, shellLauncher.CallCount);
        Assert.Equal(Options.Arguments, shellLauncher.LastUri);
    }

    [Fact]
    public void Launch_NonFullTrustUriRetryAndShellExecuteFail_ReturnsUriRetryError()
    {
        var initialFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.LaunchUriAsyncFailed,
            "LaunchUriAsync",
            "URI launch failed.");
        var retryFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.LaunchUriAsyncFailed,
            "LaunchUriAsync",
            "URI retry failed.");
        var desktopLauncher = new FakeDesktopActivationLauncher();
        var packageRegistration = new FakePackageRegistration();
        var uriProtocolLauncher = new FakeUriProtocolLauncher(initialFailure, retryFailure);
        var shellLauncher = new FakeShellUriProtocolLauncher(AppxLaunchResult.Failed(
            AppxLaunchExitCode.ShellExecuteFailed,
            "ShellExecute",
            "Shell fallback failed."));
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(UriOnlyMetadata),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.Equal(retryFailure, result);
        Assert.Equal(0, desktopLauncher.CallCount);
        Assert.Equal(1, packageRegistration.CallCount);
        Assert.Equal(2, uriProtocolLauncher.CallCount);
        Assert.Equal(1, shellLauncher.CallCount);
    }

    [Fact]
    public void Launch_NonFullTrustWithoutProtocol_ReturnsProtocolResolutionError()
    {
        var metadata = UriOnlyMetadata with { Protocol = null };
        var desktopLauncher = new FakeDesktopActivationLauncher();
        var packageRegistration = new FakePackageRegistration();
        var uriProtocolLauncher = new FakeUriProtocolLauncher();
        var shellLauncher = new FakeShellUriProtocolLauncher();
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(metadata),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher);

        var result = launcher.Launch(Options);

        Assert.False(result.Success);
        Assert.Equal(AppxLaunchExitCode.ProtocolResolutionFailed, result.ExitCode);
        Assert.Equal(0, desktopLauncher.CallCount);
        Assert.Equal(0, packageRegistration.CallCount);
        Assert.Equal(0, uriProtocolLauncher.CallCount);
        Assert.Equal(0, shellLauncher.CallCount);
    }

    [Fact]
    public void Launch_FullTrustDesktopActivation_UsesFullTrustVerificationKind()
    {
        var success = AppxLaunchResult.Succeeded("DesktopAppxActivateWithOptions");
        var verifier = new RecordingLaunchAttemptVerifier();
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(FullTrustMetadata),
            new FakeDesktopActivationLauncher(success),
            new FakePackageRegistration(),
            new FakeUriProtocolLauncher(),
            new FakeShellUriProtocolLauncher(),
            verifier);

        var result = launcher.Launch(Options);

        Assert.Equal(success, result);
        Assert.Equal([AppxLaunchVerificationKind.FullTrustActivation], verifier.VerificationKinds);
    }

    [Fact]
    public void Launch_NonFullTrustUriActivation_UsesUriVerificationKind()
    {
        var success = AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted");
        var verifier = new RecordingLaunchAttemptVerifier();
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(UriOnlyMetadata),
            new FakeDesktopActivationLauncher(),
            new FakePackageRegistration(),
            new FakeUriProtocolLauncher(success),
            new FakeShellUriProtocolLauncher(),
            verifier);

        var result = launcher.Launch(Options);

        Assert.Equal(success, result);
        Assert.Equal([AppxLaunchVerificationKind.UriActivation], verifier.VerificationKinds);
    }

    [Fact]
    public void Launch_NonFullTrustShellFallback_UsesUriVerificationKind()
    {
        var uriFailure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.LaunchUriAsyncFailed,
            "LaunchUriAsync",
            "URI launch failed.");
        var shellSuccess = AppxLaunchResult.Succeeded("ShellExecute");
        var verifier = new RecordingLaunchAttemptVerifier();
        var launcher = CreateLauncher(
            AppxManifestLaunchMetadataResult.Succeeded(UriOnlyMetadata),
            new FakeDesktopActivationLauncher(),
            new FakePackageRegistration(),
            new FakeUriProtocolLauncher(uriFailure, uriFailure),
            new FakeShellUriProtocolLauncher(shellSuccess),
            verifier);

        var result = launcher.Launch(Options);

        Assert.Equal(shellSuccess, result);
        Assert.Equal(
            [
                AppxLaunchVerificationKind.UriActivation,
                AppxLaunchVerificationKind.UriActivation,
                AppxLaunchVerificationKind.UriActivation
            ],
            verifier.VerificationKinds);
    }

    private static AppxLaunchOrchestrator CreateLauncher(
        AppxManifestLaunchMetadataResult metadataResult,
        IDesktopAppxActivationLauncher desktopLauncher,
        IAppxPackageRegistration packageRegistration,
        IAppxUriProtocolLauncher uriProtocolLauncher,
        IShellUriProtocolLauncher shellLauncher,
        IAppxLaunchAttemptVerifier? launchAttemptVerifier = null)
        => new(
            new FakeMetadataResolver(metadataResult),
            desktopLauncher,
            packageRegistration,
            uriProtocolLauncher,
            shellLauncher,
            launchAttemptVerifier ?? new PassThroughLaunchAttemptVerifier());

    private sealed class FakeMetadataResolver(AppxManifestLaunchMetadataResult result)
        : IAppxManifestLaunchMetadataResolver
    {
        public AppxManifestLaunchMetadataResult Resolve(string appxExecutablePath, string arguments) => result;
    }

    private sealed class FakeDesktopActivationLauncher(params AppxLaunchResult[] results)
        : IDesktopAppxActivationLauncher
    {
        private readonly Queue<AppxLaunchResult> _results = new(results);

        public int CallCount { get; private set; }

        public AppxManifestLaunchMetadata? LastMetadata { get; private set; }

        public string? LastArguments { get; private set; }

        public AppxLaunchResult Launch(AppxManifestLaunchMetadata metadata, string arguments)
        {
            CallCount++;
            LastMetadata = metadata;
            LastArguments = arguments;
            return _results.Dequeue();
        }
    }

    private sealed class FakeUriProtocolLauncher(params AppxLaunchResult[] results) : IAppxUriProtocolLauncher
    {
        private readonly Queue<AppxLaunchResult> _results = new(results);

        public int CallCount { get; private set; }

        public AppxUriLaunchOptions? LastOptions { get; private set; }

        public AppxLaunchResult Launch(AppxUriLaunchOptions options)
        {
            CallCount++;
            LastOptions = options;
            return _results.Dequeue();
        }
    }

    private sealed class FakeShellUriProtocolLauncher(params AppxLaunchResult[] results) : IShellUriProtocolLauncher
    {
        private readonly Queue<AppxLaunchResult> _results = new(results);

        public int CallCount { get; private set; }

        public string? LastUri { get; private set; }

        public AppxLaunchResult Launch(string uri)
        {
            CallCount++;
            LastUri = uri;
            return _results.Dequeue();
        }
    }

    private sealed class FakePackageRegistration(Exception? exception = null) : IAppxPackageRegistration
    {
        public int CallCount { get; private set; }

        public string? LastPackageFamilyName { get; private set; }

        public void RegisterPackageByFamilyName(string packageFamilyName)
        {
            CallCount++;
            LastPackageFamilyName = packageFamilyName;
            if (exception != null)
                throw exception;
        }
    }

    private sealed class PassThroughLaunchAttemptVerifier : IAppxLaunchAttemptVerifier
    {
        public AppxLaunchResult Verify(
            AppxManifestLaunchMetadata metadata,
            AppxLaunchVerificationKind verificationKind,
            Func<AppxLaunchResult> launch)
            => launch();
    }

    private sealed class RecordingLaunchAttemptVerifier : IAppxLaunchAttemptVerifier
    {
        public List<AppxLaunchVerificationKind> VerificationKinds { get; } = [];

        public AppxLaunchResult Verify(
            AppxManifestLaunchMetadata metadata,
            AppxLaunchVerificationKind verificationKind,
            Func<AppxLaunchResult> launch)
        {
            VerificationKinds.Add(verificationKind);
            return launch();
        }
    }
}
