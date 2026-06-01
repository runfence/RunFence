using System.Security.AccessControl;
using System.Text;
using RunFence.Core;
using RunFence.Acl;

namespace RunFence.PrefTrans;

public class SettingsTransferAccessGrantService(
    IGrantMutatorService grantMutatorService,
    ITraverseService traverseService,
    IGrantIntentSnapshotService grantIntentSnapshotService,
    IGrantAceService grantAceService,
    ILoggingService log)
    : ISettingsTransferAccessGrantService
{
    private GrantCleanupState? _grantCleanupState;
    private static readonly GrantIntentRestoreSnapshot EmptyRestoreSnapshot = new(null, []);

    public SettingsTransferGrantResult TryEnsureDurableAccess(
        string sid,
        string path,
        FileSystemRights rights)
    {
        var normalizedPath = Path.GetFullPath(path);

        try
        {
            var result = grantMutatorService.EnsureAccess(sid, normalizedPath, rights, confirm: null);
            return new SettingsTransferGrantResult(
                true,
                result.DatabaseModified,
                FormatWarnings(result.Warnings));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SettingsTransferGrantResult(false, false, ex.Message);
        }
    }

    public SettingsTransferGrantResult TryEnsureAccess(
        string sid,
        string path,
        FileSystemRights rights,
        bool isDirectory)
    {
        var normalizedPath = Path.GetFullPath(path);
        GrantCleanupEntry? cleanupEntry = null;

        try
        {
            cleanupEntry = PrepareTransferCleanupEntry(sid, normalizedPath, isDirectory);
            var result = grantMutatorService.EnsureAccess(sid, normalizedPath, rights, confirm: null);
            if (result.DatabaseModified && cleanupEntry != null)
                RecordCleanupEntry(cleanupEntry);

            return new SettingsTransferGrantResult(
                true,
                result.DatabaseModified,
                FormatWarnings(result.Warnings));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SettingsTransferGrantResult(false, false, ex.Message);
        }
    }

    public SettingsTransferGrantResult TryEnsureAccessForCleanup(
        string sid,
        string path,
        FileSystemRights rights,
        bool isDirectory)
    {
        var normalizedPath = Path.GetFullPath(path);
        GrantCleanupEntry? cleanupEntry = null;

        try
        {
            cleanupEntry = PrepareCleanupOnlyEntry(sid, normalizedPath, isDirectory);
            var result = grantMutatorService.EnsureTemporaryAccess(sid, normalizedPath, rights, confirm: null);
            bool accessCreated = result.GrantApplied || result.TraverseApplied;
            if (accessCreated && cleanupEntry != null)
            {
                RecordCleanupEntry(cleanupEntry with
                {
                    TargetCleanup = result.GrantApplied
                        ? TargetCleanupAction.RemoveTemporaryAce
                        : TargetCleanupAction.None
                });
            }

            return new SettingsTransferGrantResult(
                true,
                accessCreated,
                FormatWarnings(result.Warnings));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SettingsTransferGrantResult(false, false, ex.Message);
        }
    }

    public void CleanupTemporaryGrant()
    {
        var state = _grantCleanupState;
        if (state == null)
            return;

        _grantCleanupState = null;

        for (int i = state.Entries.Count - 1; i >= 0; i--)
        {
            var entry = state.Entries[i];

            if (entry.TargetCleanup == TargetCleanupAction.RestoreTrackedGrant)
            {
                try
                {
                    grantMutatorService.RestoreGrant(entry.Sid, entry.Path, isDeny: false, entry.GrantSnapshot);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to cleanup transfer grant for '{entry.Path}': {ex.Message}");
                }
            }
            else if (entry.TargetCleanup == TargetCleanupAction.RemoveTemporaryAce)
            {
                try
                {
                    grantAceService.RevertAce(entry.Path, entry.Sid, isDeny: false);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to cleanup transfer temporary grant for '{entry.Path}': {ex.Message}");
                }
            }

            if (string.IsNullOrEmpty(entry.TraversePath))
                continue;

            try
            {
                traverseService.RestoreTraverse(entry.Sid, entry.TraversePath, entry.TraverseSnapshot);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to cleanup transfer traverse for '{entry.TraversePath}': {ex.Message}");
            }
        }
    }

    private GrantCleanupEntry? PrepareTransferCleanupEntry(string sid, string normalizedPath, bool isDirectory)
    {
        if (HasCleanupEntry(sid, normalizedPath))
            return null;

        var traversePath = isDirectory ? normalizedPath : Path.GetDirectoryName(normalizedPath);
        var traverseSnapshot = string.IsNullOrEmpty(traversePath)
            ? EmptyRestoreSnapshot
            : grantIntentSnapshotService.CaptureTraverseRestoreSnapshot(sid, traversePath);
        return new GrantCleanupEntry(
            sid,
            normalizedPath,
            traversePath,
            grantIntentSnapshotService.CaptureGrantRestoreSnapshot(sid, normalizedPath, isDeny: false),
            traverseSnapshot,
            TargetCleanupAction.RestoreTrackedGrant);
    }

    private GrantCleanupEntry? PrepareCleanupOnlyEntry(string sid, string normalizedPath, bool isDirectory)
    {
        if (HasCleanupEntry(sid, normalizedPath))
            return null;

        var traversePath = isDirectory ? normalizedPath : Path.GetDirectoryName(normalizedPath);
        var traverseSnapshot = string.IsNullOrEmpty(traversePath)
            ? EmptyRestoreSnapshot
            : grantIntentSnapshotService.CaptureTraverseRestoreSnapshot(sid, traversePath);
        return new GrantCleanupEntry(
            sid,
            normalizedPath,
            traversePath,
            EmptyRestoreSnapshot,
            traverseSnapshot,
            TargetCleanupAction.None);
    }

    private void RecordCleanupEntry(GrantCleanupEntry cleanupEntry)
    {
        _grantCleanupState ??= new GrantCleanupState();
        _grantCleanupState.Entries.Add(cleanupEntry);
    }

    private bool HasCleanupEntry(string sid, string normalizedPath)
        => _grantCleanupState?.Entries.Any(entry =>
               string.Equals(entry.Sid, sid, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase)) == true;

    private static string? FormatWarnings(IReadOnlyList<GrantApplyWarning> warnings)
    {
        if (warnings.Count == 0)
            return null;

        var sb = new StringBuilder();
        foreach (var warning in warnings)
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(GrantApplyFailureFormatter.Format(warning));
        }

        return sb.ToString();
    }

    private sealed class GrantCleanupState
    {
        public List<GrantCleanupEntry> Entries { get; } = [];
    }

    private enum TargetCleanupAction
    {
        None,
        RestoreTrackedGrant,
        RemoveTemporaryAce
    }

    private sealed record GrantCleanupEntry(
        string Sid,
        string Path,
        string? TraversePath,
        GrantIntentRestoreSnapshot GrantSnapshot,
        GrantIntentRestoreSnapshot TraverseSnapshot,
        TargetCleanupAction TargetCleanup);
}
