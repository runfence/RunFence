using RunFence.Launching.Processes;

namespace RunFence.AppxLauncher;

public sealed class AppxLaunchAttemptVerifier(
    IAppxTargetProcessQuery processQuery,
    IAppxCurrentUserSidProvider currentUserSidProvider,
    IAppxLaunchVerificationClock clock) : IAppxLaunchAttemptVerifier
{
    private static readonly TimeSpan ProcessAppearanceTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);
    private readonly Dictionary<string, AppxTargetProcessSnapshot> _snapshotsByCommand = new(StringComparer.OrdinalIgnoreCase);

    public AppxLaunchResult Verify(
        AppxManifestLaunchMetadata metadata,
        AppxLaunchVerificationKind verificationKind,
        Func<AppxLaunchResult> launch)
    {
        AppxTargetProcessSnapshot targetProcessSnapshot;
        try
        {
            targetProcessSnapshot = GetTargetProcessSnapshot(metadata.Command);
        }
        catch (Exception ex)
        {
            return AppxLaunchResult.Failed(
                AppxLaunchExitCode.TargetProcessVerificationFailed,
                "VerifyCreatedProcess",
                ex);
        }

        var launchResult = launch();
        if (!launchResult.Success)
            return launchResult;

        string? expectedOwnerSid;
        try
        {
            expectedOwnerSid = currentUserSidProvider.GetCurrentUserSid();
        }
        catch (Exception ex)
        {
            return AppxLaunchResult.Failed(
                AppxLaunchExitCode.TargetProcessVerificationFailed,
                "VerifyCreatedProcess",
                ex);
        }

        if (string.IsNullOrWhiteSpace(expectedOwnerSid))
        {
            return AppxLaunchResult.Failed(
                AppxLaunchExitCode.TargetProcessVerificationFailed,
                "VerifyCreatedProcess",
                "Could not determine AppX launch helper owner SID.");
        }

        var deadline = clock.UtcNow + ProcessAppearanceTimeout;
        var targetFileName = Path.GetFileName(metadata.Command);
        var allowExistingProcess = verificationKind switch
        {
            AppxLaunchVerificationKind.UriActivation => true,
            AppxLaunchVerificationKind.FullTrustActivation => !metadata.SupportsMultipleInstances,
            _ => false
        };
        var wrongOwnerSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ignoredExistingProcesses = new HashSet<AppxTargetProcessInfo>();
        var sawExpectedOwnerExistingProcess = false;
        var sawUnconfirmedOwner = false;
        IReadOnlyList<AppxTargetProcessInfo> currentProcesses = [];

        while (true)
        {
            try
            {
                currentProcesses = processQuery.GetTargetProcesses(metadata.Command);
            }
            catch (Exception ex)
            {
                return AppxLaunchResult.Failed(
                    AppxLaunchExitCode.TargetProcessVerificationFailed,
                    "VerifyCreatedProcess",
                    ex);
            }

            foreach (var process in currentProcesses)
            {
                var isPreExistingProcess = targetProcessSnapshot.Contains(process);
                if (isPreExistingProcess && !allowExistingProcess)
                    continue;
                if (isPreExistingProcess && ignoredExistingProcesses.Contains(process))
                    continue;

                ProcessOwnerInfo owner;
                try
                {
                    owner = processQuery.GetProcessOwner(process.ProcessId, expectedOwnerSid);
                }
                catch (Exception ex)
                {
                    RefreshSnapshot(metadata.Command, currentProcesses);
                    return AppxLaunchResult.Failed(
                        AppxLaunchExitCode.TargetProcessVerificationFailed,
                        "VerifyCreatedProcess",
                        ex);
                }

                if (owner.Match == ProcessOwnerMatch.ExpectedOwner)
                {
                    if (isPreExistingProcess)
                    {
                        sawExpectedOwnerExistingProcess = true;
                        continue;
                    }

                    return launchResult;
                }

                if (isPreExistingProcess)
                {
                    ignoredExistingProcesses.Add(process);
                    continue;
                }

                if (owner.Match == ProcessOwnerMatch.Unknown)
                {
                    sawUnconfirmedOwner = true;
                    continue;
                }

                wrongOwnerSids.Add(owner.OwnerSid ?? "inaccessible non-current owner");
            }

            if (wrongOwnerSids.Count > 0)
            {
                RefreshSnapshot(metadata.Command, currentProcesses);

                return AppxLaunchResult.Failed(
                    AppxLaunchExitCode.TargetProcessVerificationFailed,
                    "VerifyCreatedProcess",
                    $"No new '{targetFileName}' process owned by expected SID '{expectedOwnerSid}' appeared after stage " +
                    $"'{launchResult.Stage}' reported success. Observed owner SIDs: {string.Join(", ", wrongOwnerSids)}.");
            }

            if (sawExpectedOwnerExistingProcess)
                return launchResult;

            var remaining = deadline - clock.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            clock.Sleep(remaining < PollInterval ? remaining : PollInterval);
        }

        RefreshSnapshot(metadata.Command, currentProcesses);
        var baseMessage =
            $"No new '{targetFileName}' process owned by expected SID '{expectedOwnerSid}' appeared within 200 ms " +
            $"after stage '{launchResult.Stage}' reported success.";

        if (sawUnconfirmedOwner)
        {
            return AppxLaunchResult.Failed(
                AppxLaunchExitCode.TargetProcessVerificationFailed,
                "VerifyCreatedProcess",
                $"{baseMessage} A new matching process appeared, but its owner SID could not be verified.");
        }

        return AppxLaunchResult.Failed(
            AppxLaunchExitCode.TargetProcessVerificationFailed,
            "VerifyCreatedProcess",
            baseMessage);
    }

    private AppxTargetProcessSnapshot GetTargetProcessSnapshot(string command)
    {
        if (_snapshotsByCommand.TryGetValue(command, out var snapshot))
            return snapshot;

        snapshot = new AppxTargetProcessSnapshot(processQuery.GetTargetProcesses(command));
        _snapshotsByCommand.Add(command, snapshot);
        return snapshot;
    }

    private void RefreshSnapshot(string command, IReadOnlyList<AppxTargetProcessInfo> processes)
    {
        _snapshotsByCommand[command] = new AppxTargetProcessSnapshot(processes);
    }
}
