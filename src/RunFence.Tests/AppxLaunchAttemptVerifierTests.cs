using RunFence.AppxLauncher;
using RunFence.Core;
using RunFence.Launching.Processes;
using Xunit;

namespace RunFence.Tests;

public sealed class AppxLaunchAttemptVerifierTests
{
    private static readonly AppxManifestLaunchMetadata Metadata =
        new(
            "OpenAI.Codex_2p2nqsd0c76g0",
            "OpenAI.Codex_2p2nqsd0c76g0!App",
            @"C:\Program Files\WindowsApps\OpenAI.Codex\app\Codex.exe",
            "codex",
            true);

    private static readonly AppxManifestLaunchMetadata MultiInstanceMetadata = Metadata with
    {
        SupportsMultipleInstances = true
    };

    [Fact]
    public void Verify_LaunchFails_ReturnsLaunchFailureWithoutWaitingAfterLaunch()
    {
        var processQuery = new StubTargetProcessQuery([]);
        var clock = new ManualLaunchVerificationClock();
        var verifier = new AppxLaunchAttemptVerifier(
            processQuery,
            new StubCurrentUserSidProvider("S-1-5-21-1-2-3-1001"),
            clock);
        var failure = AppxLaunchResult.Failed(
            AppxLaunchExitCode.DesktopAppxActivationFailed,
            "DesktopAppxActivateWithOptions",
            "Activation failed.");

        var result = verifier.Verify(Metadata, AppxLaunchVerificationKind.FullTrustActivation, () => failure);

        Assert.Equal(failure, result);
        Assert.Equal(1, processQuery.CallCount);
        Assert.Equal(TimeSpan.Zero, clock.TotalSlept);
    }

    [Fact]
    public void Verify_LaunchSucceedsAndNewExpectedOwnerProcessAppears_ReturnsLaunchSuccess()
    {
        var expectedSid = "S-1-5-21-1-2-3-1001";
        var success = AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted");
        var processQuery = new StubTargetProcessQuery(
            [],
            [Process(10)])
            .WithOwner(10, new ProcessOwnerInfo(ProcessOwnerMatch.ExpectedOwner, expectedSid));
        var verifier = new AppxLaunchAttemptVerifier(
            processQuery,
            new StubCurrentUserSidProvider(expectedSid),
            new ManualLaunchVerificationClock());

        var result = verifier.Verify(Metadata, AppxLaunchVerificationKind.FullTrustActivation, () => success);

        Assert.Equal(success, result);
        Assert.Equal(2, processQuery.CallCount);
    }

    [Fact]
    public void Verify_FullTrustLaunchSucceedsWithoutAcceptedProcessWithinTimeout_ReturnsVerificationFailure()
    {
        var processQuery = new StubTargetProcessQuery([]);
        var clock = new ManualLaunchVerificationClock();
        var verifier = new AppxLaunchAttemptVerifier(
            processQuery,
            new StubCurrentUserSidProvider("S-1-5-21-1-2-3-1001"),
            clock);

        var result = verifier.Verify(
            Metadata,
            AppxLaunchVerificationKind.FullTrustActivation,
            () => AppxLaunchResult.Succeeded("DesktopAppxActivateWithOptions"));

        Assert.False(result.Success);
        Assert.Equal(AppxLaunchExitCode.TargetProcessVerificationFailed, result.ExitCode);
        Assert.Equal("VerifyCreatedProcess", result.Stage);
        Assert.Contains("within 200 ms", result.Message, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMilliseconds(200), clock.TotalSlept);
    }

    [Fact]
    public void Verify_LaunchSucceedsAndOnlyWrongOwnerProcessAppears_ReturnsVerificationFailure()
    {
        var processQuery = new StubTargetProcessQuery(
            [],
            [Process(10)])
            .WithOwner(10, new ProcessOwnerInfo(ProcessOwnerMatch.DifferentOwner, "S-1-5-21-1-2-3-2002"));
        var clock = new ManualLaunchVerificationClock();
        var verifier = new AppxLaunchAttemptVerifier(
            processQuery,
            new StubCurrentUserSidProvider("S-1-5-21-1-2-3-1001"),
            clock);

        var result = verifier.Verify(
            Metadata,
            AppxLaunchVerificationKind.FullTrustActivation,
            () => AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted"));

        Assert.False(result.Success);
        Assert.Equal(AppxLaunchExitCode.TargetProcessVerificationFailed, result.ExitCode);
        Assert.Contains("Observed owner SIDs: S-1-5-21-1-2-3-2002", result.Message, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.Zero, clock.TotalSlept);
    }

    [Fact]
    public void Verify_WrongOwnerFailure_RefreshesSnapshotForNextAttempt()
    {
        var wrongOwnerProcess = new AppxTargetProcessInfo(
            10,
            new DateTime(2026, 5, 31, 1, 0, 0, DateTimeKind.Utc),
            Metadata.Command);
        var processQuery = new StubTargetProcessQuery([], [wrongOwnerProcess])
            .WithOwner(10, new ProcessOwnerInfo(ProcessOwnerMatch.DifferentOwner, "S-1-5-21-1-2-3-2002"));
        var clock = new ManualLaunchVerificationClock();
        var verifier = new AppxLaunchAttemptVerifier(
            processQuery,
            new StubCurrentUserSidProvider("S-1-5-21-1-2-3-1001"),
            clock);

        var firstResult = verifier.Verify(
            MultiInstanceMetadata,
            AppxLaunchVerificationKind.FullTrustActivation,
            () => AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted"));
        var secondResult = verifier.Verify(
            MultiInstanceMetadata,
            AppxLaunchVerificationKind.FullTrustActivation,
            () => AppxLaunchResult.Succeeded("ShellExecute"));

        Assert.False(firstResult.Success);
        Assert.Contains("Observed owner SIDs: S-1-5-21-1-2-3-2002", firstResult.Message, StringComparison.Ordinal);
        Assert.False(secondResult.Success);
        Assert.Contains("within 200 ms", secondResult.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Observed owner SIDs", secondResult.Message, StringComparison.Ordinal);
        Assert.Equal(1, processQuery.OwnerLookupCount);
        Assert.Equal(TimeSpan.FromMilliseconds(200), clock.TotalSlept);
    }

    [Fact]
    public void Verify_TimeoutFailure_RefreshesSnapshotForNextAttempt()
    {
        var unconfirmedProcess = Process(10);
        var expectedSid = "S-1-5-21-1-2-3-1001";
        var processQuery = new StubTargetProcessQuery([], [unconfirmedProcess]);
        var clock = new ManualLaunchVerificationClock();
        var verifier = new AppxLaunchAttemptVerifier(
            processQuery,
            new StubCurrentUserSidProvider(expectedSid),
            clock);

        var firstResult = verifier.Verify(
            MultiInstanceMetadata,
            AppxLaunchVerificationKind.FullTrustActivation,
            () => AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted"));
        var ownerLookupCountAfterFirstAttempt = processQuery.OwnerLookupCount;
        processQuery.WithOwner(10, new ProcessOwnerInfo(ProcessOwnerMatch.ExpectedOwner, expectedSid));
        var secondResult = verifier.Verify(
            MultiInstanceMetadata,
            AppxLaunchVerificationKind.FullTrustActivation,
            () => AppxLaunchResult.Succeeded("ShellExecute"));

        Assert.False(firstResult.Success);
        Assert.Contains("owner SID could not be verified", firstResult.Message, StringComparison.Ordinal);
        Assert.False(secondResult.Success);
        Assert.Contains("within 200 ms", secondResult.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("owner SID could not be verified", secondResult.Message, StringComparison.Ordinal);
        Assert.Equal(ownerLookupCountAfterFirstAttempt, processQuery.OwnerLookupCount);
    }

    [Fact]
    public void Verify_LaunchSucceedsAndNewInaccessibleNonCurrentOwnerProcessAppears_ReturnsVerificationFailure()
    {
        var processQuery = new StubTargetProcessQuery([], [Process(10)])
            .WithOwner(10, new ProcessOwnerInfo(ProcessOwnerMatch.InaccessibleDifferentOwner, null));
        var clock = new ManualLaunchVerificationClock();
        var verifier = new AppxLaunchAttemptVerifier(
            processQuery,
            new StubCurrentUserSidProvider("S-1-5-21-1-2-3-1001"),
            clock);

        var result = verifier.Verify(
            Metadata,
            AppxLaunchVerificationKind.FullTrustActivation,
            () => AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted"));

        Assert.False(result.Success);
        Assert.Equal(AppxLaunchExitCode.TargetProcessVerificationFailed, result.ExitCode);
        Assert.Contains("Observed owner SIDs: inaccessible non-current owner", result.Message, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.Zero, clock.TotalSlept);
    }

    [Fact]
    public void Verify_FullTrustMultiInstanceLaunchWithOnlyPreexistingExpectedOwnerProcess_ReturnsVerificationFailure()
    {
        var expectedSid = "S-1-5-21-1-2-3-1001";
        var existingProcess = new AppxTargetProcessInfo(
            10,
            new DateTime(2026, 5, 31, 1, 0, 0, DateTimeKind.Utc),
            Metadata.Command);
        var processQuery = new StubTargetProcessQuery([existingProcess], [existingProcess])
            .WithOwner(10, new ProcessOwnerInfo(ProcessOwnerMatch.ExpectedOwner, expectedSid));
        var verifier = new AppxLaunchAttemptVerifier(
            processQuery,
            new StubCurrentUserSidProvider(expectedSid),
            new ManualLaunchVerificationClock());

        var result = verifier.Verify(
            MultiInstanceMetadata,
            AppxLaunchVerificationKind.FullTrustActivation,
            () => AppxLaunchResult.Succeeded("DesktopAppxActivateWithOptions"));

        Assert.False(result.Success);
        Assert.Equal(AppxLaunchExitCode.TargetProcessVerificationFailed, result.ExitCode);
    }

    [Fact]
    public void Verify_FullTrustSingleInstanceLaunchWithPreexistingExpectedOwnerProcess_ReturnsLaunchSuccess()
    {
        var expectedSid = "S-1-5-21-1-2-3-1001";
        var success = AppxLaunchResult.Succeeded("DesktopAppxActivateWithOptions");
        var existingProcess = new AppxTargetProcessInfo(
            10,
            new DateTime(2026, 5, 31, 1, 0, 0, DateTimeKind.Utc),
            Metadata.Command);
        var processQuery = new StubTargetProcessQuery([existingProcess], [existingProcess])
            .WithOwner(10, new ProcessOwnerInfo(ProcessOwnerMatch.ExpectedOwner, expectedSid));
        var verifier = new AppxLaunchAttemptVerifier(
            processQuery,
            new StubCurrentUserSidProvider(expectedSid),
            new ManualLaunchVerificationClock());

        var result = verifier.Verify(
            Metadata,
            AppxLaunchVerificationKind.FullTrustActivation,
            () => success);

        Assert.Equal(success, result);
    }

    [Fact]
    public void Verify_UriLaunchWithPreexistingExpectedOwnerProcess_ReturnsLaunchSuccess()
    {
        var expectedSid = "S-1-5-21-1-2-3-1001";
        var success = AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted");
        var existingProcess = new AppxTargetProcessInfo(
            10,
            new DateTime(2026, 5, 31, 1, 0, 0, DateTimeKind.Utc),
            Metadata.Command);
        var processQuery = new StubTargetProcessQuery([existingProcess], [existingProcess])
            .WithOwner(10, new ProcessOwnerInfo(ProcessOwnerMatch.ExpectedOwner, expectedSid));
        var verifier = new AppxLaunchAttemptVerifier(
            processQuery,
            new StubCurrentUserSidProvider(expectedSid),
            new ManualLaunchVerificationClock());

        var result = verifier.Verify(
            MultiInstanceMetadata,
            AppxLaunchVerificationKind.UriActivation,
            () => success);

        Assert.Equal(success, result);
    }

    [Fact]
    public void Verify_UriLaunchWithOnlyPreexistingDifferentOwnerProcess_ReturnsVerificationFailure()
    {
        var existingProcess = new AppxTargetProcessInfo(
            10,
            new DateTime(2026, 5, 31, 1, 0, 0, DateTimeKind.Utc),
            Metadata.Command);
        var clock = new ManualLaunchVerificationClock();
        var processQuery = new StubTargetProcessQuery([existingProcess], [existingProcess])
            .WithOwner(10, new ProcessOwnerInfo(ProcessOwnerMatch.DifferentOwner, "S-1-5-21-1-2-3-2002"));
        var verifier = new AppxLaunchAttemptVerifier(
            processQuery,
            new StubCurrentUserSidProvider("S-1-5-21-1-2-3-1001"),
            clock);

        var result = verifier.Verify(
            Metadata,
            AppxLaunchVerificationKind.UriActivation,
            () => AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted"));

        Assert.False(result.Success);
        Assert.Equal(AppxLaunchExitCode.TargetProcessVerificationFailed, result.ExitCode);
        Assert.Contains("within 200 ms", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Observed owner SIDs", result.Message, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMilliseconds(200), clock.TotalSlept);
        Assert.Equal(1, processQuery.OwnerLookupCount);
    }

    [Fact]
    public void Verify_UriLaunchWithPreexistingExpectedOwnerAndNewDifferentOwnerProcess_ReturnsVerificationFailure()
    {
        var expectedSid = "S-1-5-21-1-2-3-1001";
        var existingProcess = new AppxTargetProcessInfo(
            10,
            new DateTime(2026, 5, 31, 1, 0, 0, DateTimeKind.Utc),
            Metadata.Command);
        var newWrongOwnerProcess = new AppxTargetProcessInfo(
            20,
            new DateTime(2026, 5, 31, 1, 0, 1, DateTimeKind.Utc),
            Metadata.Command);
        var processQuery = new StubTargetProcessQuery(
            [existingProcess],
            [existingProcess, newWrongOwnerProcess])
            .WithOwner(10, new ProcessOwnerInfo(ProcessOwnerMatch.ExpectedOwner, expectedSid))
            .WithOwner(20, new ProcessOwnerInfo(ProcessOwnerMatch.DifferentOwner, "S-1-5-21-1-2-3-2002"));
        var verifier = new AppxLaunchAttemptVerifier(
            processQuery,
            new StubCurrentUserSidProvider(expectedSid),
            new ManualLaunchVerificationClock());

        var result = verifier.Verify(
            Metadata,
            AppxLaunchVerificationKind.UriActivation,
            () => AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted"));

        Assert.False(result.Success);
        Assert.Equal(AppxLaunchExitCode.TargetProcessVerificationFailed, result.ExitCode);
        Assert.Contains("Observed owner SIDs: S-1-5-21-1-2-3-2002", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_CurrentUserSidProviderThrows_ReturnsVerificationFailure()
    {
        var processQuery = new StubTargetProcessQuery([]);
        var verifier = new AppxLaunchAttemptVerifier(
            processQuery,
            new ThrowingCurrentUserSidProvider(),
            new ManualLaunchVerificationClock());

        var result = verifier.Verify(
            Metadata,
            AppxLaunchVerificationKind.FullTrustActivation,
            () => AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted"));

        Assert.False(result.Success);
        Assert.Equal(AppxLaunchExitCode.TargetProcessVerificationFailed, result.ExitCode);
        Assert.Equal("VerifyCreatedProcess", result.Stage);
        Assert.Equal(new InvalidOperationException("SID lookup failed.").HResult, result.HResult);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Verify_ProcessQueryThrows_ReturnsVerificationFailure(int throwingCall)
    {
        var verifier = new AppxLaunchAttemptVerifier(
            new ThrowingTargetProcessQuery(throwingCall),
            new StubCurrentUserSidProvider("S-1-5-21-1-2-3-1001"),
            new ManualLaunchVerificationClock());

        var result = verifier.Verify(
            Metadata,
            AppxLaunchVerificationKind.FullTrustActivation,
            () => AppxLaunchResult.Succeeded("LaunchUriAsyncCompleted"));

        Assert.False(result.Success);
        Assert.Equal(AppxLaunchExitCode.TargetProcessVerificationFailed, result.ExitCode);
        Assert.Equal("VerifyCreatedProcess", result.Stage);
        Assert.Equal(new InvalidOperationException("Process query failed.").HResult, result.HResult);
    }

    private sealed class StubTargetProcessQuery(params AppxTargetProcessInfo[][] snapshots) : IAppxTargetProcessQuery
    {
        private readonly Dictionary<int, ProcessOwnerInfo> _owners = [];

        public int CallCount { get; private set; }

        public int OwnerLookupCount { get; private set; }

        public StubTargetProcessQuery WithOwner(int processId, ProcessOwnerInfo owner)
        {
            _owners[processId] = owner;
            return this;
        }

        public IReadOnlyList<AppxTargetProcessInfo> GetTargetProcesses(string executablePath)
        {
            var index = Math.Min(CallCount, snapshots.Length - 1);
            CallCount++;
            return snapshots.Length == 0 ? [] : snapshots[index];
        }

        public ProcessOwnerInfo GetProcessOwner(int processId, string expectedOwnerSid)
        {
            OwnerLookupCount++;
            return _owners.GetValueOrDefault(processId, new ProcessOwnerInfo(ProcessOwnerMatch.Unknown, null));
        }
    }

    private sealed class ThrowingTargetProcessQuery(int throwingCall) : IAppxTargetProcessQuery
    {
        private int _callCount;

        public IReadOnlyList<AppxTargetProcessInfo> GetTargetProcesses(string executablePath)
        {
            _callCount++;
            if (_callCount == throwingCall)
                throw new InvalidOperationException("Process query failed.");

            return [];
        }

        public ProcessOwnerInfo GetProcessOwner(int processId, string expectedOwnerSid) =>
            new(ProcessOwnerMatch.Unknown, null);
    }

    private static AppxTargetProcessInfo Process(int processId) =>
        new(processId, new DateTime(2026, 5, 31, 1, 0, 0, DateTimeKind.Utc), Metadata.Command);

    private sealed class StubCurrentUserSidProvider(string? sid) : IAppxCurrentUserSidProvider
    {
        public string? GetCurrentUserSid() => sid;
    }

    private sealed class ThrowingCurrentUserSidProvider : IAppxCurrentUserSidProvider
    {
        public string? GetCurrentUserSid() => throw new InvalidOperationException("SID lookup failed.");
    }

    private sealed class ManualLaunchVerificationClock : IAppxLaunchVerificationClock
    {
        public DateTime UtcNow { get; private set; } = new(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);

        public TimeSpan TotalSlept { get; private set; }

        public void Sleep(TimeSpan duration)
        {
            TotalSlept += duration;
            UtcNow += duration;
        }
    }
}
