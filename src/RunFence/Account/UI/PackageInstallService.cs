using RunFence.Core;
using RunFence.Launch;

namespace RunFence.Account.UI;

/// <summary>
/// Installs packages for an account by creating a PowerShell script, launching it, and
/// tracking the launched process until completion.
/// </summary>
public class PackageInstallService(
    IPackageInstallLauncher packageInstallLauncher,
    IPackageInstallScriptStore packageInstallScriptStore,
    AccountToolResolver toolResolver,
    IWindowsTerminalAccountStateService windowsTerminalAccountStateService,
    IWindowsTerminalDeploymentService windowsTerminalDeploymentService)
    : IPackageInstallService
{
    private readonly object _pendingInstallLock = new();
    private readonly Dictionary<string, PendingInstallOperation> _pendingInstallsBySid = new(StringComparer.OrdinalIgnoreCase);

    public bool IsPackageInstalled(InstallablePackage package, string sid)
    {
        if (package == KnownPackages.WindowsTerminal)
            return windowsTerminalAccountStateService.IsInstalledForAccount(sid);

        if (package.DetectExeName != null)
            return toolResolver.ResolveWindowsAppsExe(sid, package.DetectExeName) != null;

        if (package.DetectProfileRelativePath != null)
        {
            var profileRoot = toolResolver.GetProfileRoot(sid);
            return profileRoot != null && File.Exists(Path.Combine(profileRoot, package.DetectProfileRelativePath));
        }

        return false;
    }

    public async Task<IReadOnlyList<string>> InstallPackagesAsync(
        IReadOnlyList<InstallablePackage> packages,
        AccountLaunchIdentity identity,
        CancellationToken cancellationToken)
    {
        if (packages.Count == 0)
            return [];

        string? scriptPath = null;
        var operation = ReservePendingInstall(identity.Sid);
        try
        {
            var packagesToInstall = KnownPackages.ExpandWithDependencies(packages);
            if (packagesToInstall.Contains(KnownPackages.WindowsTerminal))
                await windowsTerminalDeploymentService.EnsureSharedDeploymentReadyAsync(cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var body = string.Join("\n", packagesToInstall.Select(p => p.PowerShellCommand));
            var cmd = $"try {{\n{body}\n}} finally {{\n" +
                      "Remove-Item $PSCommandPath -Force -ErrorAction SilentlyContinue\n}";

            scriptPath = packageInstallScriptStore.CreateScript(cmd, identity.Sid);
            operation.AttachScriptPath(scriptPath);

            cancellationToken.ThrowIfCancellationRequested();
            var launchResult = packageInstallLauncher.Launch(scriptPath, identity);
            operation.AttachProcess(launchResult.Process);
            return launchResult.MaintenanceWarnings;
        }
        catch
        {
            CleanupReservation(identity.Sid, operation, scriptPath);
            throw;
        }
    }

    /// <summary>
    /// Waits for the install script launched by <see cref="InstallPackagesAsync"/> to complete by polling
    /// the launched PowerShell process. Returns when the process exits, when <paramref name="timeout"/> elapses (if specified), or when
    /// <paramref name="ct"/> is cancelled (user clicked Cancel on the progress form).
    /// </summary>
    public async Task WaitForInstallCompletionAsync(string sid, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var deadline = timeout.HasValue ? DateTime.UtcNow + timeout.Value : (DateTime?)null;
        PendingInstallOperation? observedOperation = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            lock (_pendingInstallLock)
            {
                if (observedOperation == null)
                {
                    if (!_pendingInstallsBySid.TryGetValue(sid, out observedOperation))
                        return;
                }
                else if (!_pendingInstallsBySid.TryGetValue(sid, out var currentOperation)
                         || !ReferenceEquals(currentOperation, observedOperation))
                {
                    if (!observedOperation.HasProcessAttachedOrCompleted)
                        return;
                }
            }

            if (observedOperation.TryGetCompletionExitCode(out var exitCode))
            {
                CompletePendingInstall(sid, observedOperation);
                if (exitCode != 0)
                    throw new InvalidOperationException(
                        $"The package install window closed with exit code {exitCode}.");
                return;
            }

            if (!observedOperation.HasProcessAttached)
            {
                if (deadline.HasValue && DateTime.UtcNow >= deadline.Value)
                    return;

                await Task.Delay(200, ct);
                continue;
            }

            if (deadline.HasValue && DateTime.UtcNow >= deadline.Value)
                return;

            await Task.Delay(200, ct);
        }
    }

    /// <summary>
    /// Deletes install scripts older than 1 hour from the program data directory.
    /// Called at startup to clean up scripts left behind by previous crashes.
    /// </summary>
    public void CleanupStaleScripts()
        => packageInstallScriptStore.CleanupStaleScripts();

    private void CompletePendingInstall(string sid, PendingInstallOperation operation)
    {
        if (RemovePendingInstallIfSame(sid, operation))
            CompleteRemovedOperation(sid, operation);
    }

    private PendingInstallOperation ReservePendingInstall(string sid)
    {
        PendingInstallOperation? completedOperation = null;
        PendingInstallOperation reservation;
        lock (_pendingInstallLock)
        {
            if (_pendingInstallsBySid.TryGetValue(sid, out var existingOperation))
            {
                if (existingOperation.IsActiveOrLaunching)
                {
                    throw new InvalidOperationException(
                        $"A package install is already running for SID '{sid}'.");
                }

                if (ReferenceEquals(existingOperation, _pendingInstallsBySid.GetValueOrDefault(sid)))
                {
                    _pendingInstallsBySid.Remove(sid);
                    completedOperation = existingOperation;
                }
            }

            reservation = new PendingInstallOperation();
            _pendingInstallsBySid.Add(sid, reservation);
        }

        if (completedOperation != null)
            CompleteRemovedOperation(sid, completedOperation);

        return reservation;
    }

    private void CleanupReservation(string sid, PendingInstallOperation operation, string? fallbackScriptPath)
    {
        if (!RemovePendingInstallIfSame(sid, operation))
            return;

        CompleteRemovedOperation(sid, operation, fallbackScriptPath);
    }

    private bool RemovePendingInstallIfSame(string sid, PendingInstallOperation operation)
    {
        lock (_pendingInstallLock)
        {
            if (!_pendingInstallsBySid.TryGetValue(sid, out var currentOperation) || !ReferenceEquals(currentOperation, operation))
                return false;

            _pendingInstallsBySid.Remove(sid);
            return true;
        }
    }

    private void CompleteRemovedOperation(string sid, PendingInstallOperation operation, string? fallbackScriptPath = null)
    {
        operation.TryGetCompletionExitCode(out _);
        operation.Dispose();
        var scriptPath = operation.ScriptPath ?? fallbackScriptPath;
        if (!string.IsNullOrEmpty(scriptPath))
            packageInstallScriptStore.Delete(scriptPath);
    }

    private sealed class PendingInstallOperation : IDisposable
    {
        private readonly object _lock = new();
        private int? _completionExitCode;

        public string? ScriptPath { get; private set; }
        public IInstallProcess? Process { get; private set; }
        public bool HasProcessAttached
        {
            get
            {
                lock (_lock)
                    return Process != null;
            }
        }

        public bool HasProcessAttachedOrCompleted
        {
            get
            {
                lock (_lock)
                    return Process != null || _completionExitCode.HasValue;
            }
        }

        public bool IsActiveOrLaunching
        {
            get
            {
                lock (_lock)
                {
                    if (_completionExitCode.HasValue)
                        return false;

                    return Process == null || !Process.HasExited;
                }
            }
        }

        public void AttachScriptPath(string scriptPath)
        {
            ScriptPath = scriptPath;
        }

        public void AttachProcess(IInstallProcess process)
        {
            lock (_lock)
                Process = process;
        }

        public bool TryGetCompletionExitCode(out int exitCode)
        {
            lock (_lock)
            {
                if (_completionExitCode.HasValue)
                {
                    exitCode = _completionExitCode.Value;
                    return true;
                }

                if (Process == null || !Process.HasExited)
                {
                    exitCode = default;
                    return false;
                }

                _completionExitCode = Process.ExitCode;
                exitCode = _completionExitCode.Value;
                return true;
            }
        }

        public void Dispose()
        {
            lock (_lock)
                Process?.Dispose();
        }
    }
}
