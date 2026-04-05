using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles Phase 1 (NTFS removes) and Phase 3 (NTFS adds/modifications/traverse) of the Apply pipeline.
/// All NTFS operations run on a background thread via Task.Run.
/// </summary>
public class AclManagerNtfsApplier
{
    private readonly IGrantedPathAclService _aclService;
    private readonly ILoggingService _log;
    private readonly AclManagerGrantsHelper _grantsHelper;
    private readonly AclManagerTraverseHelper _traverseHelper;
    private readonly GrantEntryNtfsOperations _ntfsOps;

    public AclManagerNtfsApplier(
        IGrantedPathAclService aclService,
        ILoggingService log,
        AclManagerGrantsHelper grantsHelper,
        AclManagerTraverseHelper traverseHelper,
        GrantEntryNtfsOperations ntfsOps)
    {
        _aclService = aclService;
        _log = log;
        _grantsHelper = grantsHelper;
        _traverseHelper = traverseHelper;
        _ntfsOps = ntfsOps;
    }

    /// <summary>
    /// Phase 1: Reverts NTFS ACEs for all pending removes and traverse removes (background).
    /// Returns the sets of successfully reverted paths and the updated progress counter value.
    /// </summary>
    public async Task<NtfsRemoveResult> RemoveEntriesAsync(
        List<GrantedPathEntry> pendingRemoves,
        List<GrantedPathEntry> pendingTraverseRemoves,
        List<(string Path, string Error)> errors,
        IWin32Window owner,
        ToolStripProgressBar progressBar,
        int current,
        int total)
    {
        var successfulRemoves = new HashSet<(string Path, bool IsDeny)>(new GrantPathKeyComparer());
        var successfulTraverseRemoves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int c = current;

        await Task.Run(() =>
        {
            foreach (var entry in pendingRemoves)
            {
                try
                {
                    _ntfsOps.RevertGrantEntry(entry);
                    successfulRemoves.Add((entry.Path, entry.IsDeny));
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to revert grant ACE for '{entry.Path}'", ex);
                    errors.Add((entry.Path, ex.Message));
                }

                ReportProgress(owner, progressBar, ++c, total);
            }

            foreach (var entry in pendingTraverseRemoves)
            {
                try
                {
                    _traverseHelper.RevertTraverseEntry(entry);
                    successfulTraverseRemoves.Add(entry.Path);
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to revert traverse ACE for '{entry.Path}'", ex);
                    errors.Add((entry.Path, ex.Message));
                }

                ReportProgress(owner, progressBar, ++c, total);
            }
        });

        return new NtfsRemoveResult(successfulRemoves, successfulTraverseRemoves, c);
    }

    /// <summary>
    /// Phase 3: Applies NTFS ACEs for all pending adds, traverse adds, modifications, and traverse fixes (background).
    /// Returns path tracking data for newly-added traverse entries and modification traverse entries.
    /// </summary>
    public async Task<NtfsApplyResult> ApplyEntriesAsync(
        List<GrantedPathEntry> pendingAdds,
        List<GrantedPathEntry> pendingTraverseAdds,
        List<GrantedPathEntry> pendingModifications,
        List<GrantedPathEntry> pendingTraverseFixes,
        string sid,
        bool isContainer,
        List<(string Path, string Error)> errors,
        IWin32Window owner,
        ToolStripProgressBar progressBar,
        int current,
        int total)
    {
        var traverseAddVisitedPaths = new List<(GrantedPathEntry Entry, List<string> VisitedPaths)>();
        var traverseNtfsAppliedForModifications = new List<(GrantedPathEntry Entry, List<string> VisitedPaths)>();
        int c = current;

        await Task.Run(() =>
        {
            foreach (var entry in pendingAdds)
            {
                try
                {
                    ApplyGrantEntry(entry, sid, isContainer);
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to apply grant ACE for '{entry.Path}'", ex);
                    errors.Add((entry.Path, ex.Message));
                }

                ReportProgress(owner, progressBar, ++c, total);
            }

            foreach (var entry in pendingTraverseAdds)
            {
                try
                {
                    var visitedPaths = _traverseHelper.ApplyTraverseEntryNow(entry);
                    if (visitedPaths.Count > 0)
                        traverseAddVisitedPaths.Add((entry, visitedPaths));
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to apply traverse ACE for '{entry.Path}'", ex);
                    errors.Add((entry.Path, ex.Message));
                }

                ReportProgress(owner, progressBar, ++c, total);
            }

            foreach (var entry in pendingModifications)
            {
                try
                {
                    ApplyModification(entry, sid, isContainer);
                    if (!entry.IsDeny)
                    {
                        var traversePath = TraverseAutoManager.GetTraversePath(entry.Path);
                        if (traversePath != null)
                        {
                            var traverseEntry = new GrantedPathEntry { Path = traversePath, IsTraverseOnly = true };
                            var visitedPaths = _traverseHelper.ApplyTraverseEntryNow(traverseEntry);
                            if (visitedPaths.Count > 0)
                                traverseNtfsAppliedForModifications.Add((traverseEntry, visitedPaths));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to apply modification for '{entry.Path}'", ex);
                    errors.Add((entry.Path, ex.Message));
                }

                ReportProgress(owner, progressBar, ++c, total);
            }

            foreach (var entry in pendingTraverseFixes)
            {
                try
                {
                    _traverseHelper.FixTraverseEntryNow(entry);
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to re-apply traverse ACE for '{entry.Path}'", ex);
                    errors.Add((entry.Path, ex.Message));
                }

                ReportProgress(owner, progressBar, ++c, total);
            }
        });

        return new NtfsApplyResult(traverseAddVisitedPaths, traverseNtfsAppliedForModifications);
    }

    private void ApplyGrantEntry(GrantedPathEntry entry, string sid, bool isContainer)
    {
        if (entry.SavedRights == null)
        {
            _ntfsOps.ApplyInitialGrant(entry);
            return;
        }

        if (entry.IsDeny)
        {
            _aclService.ApplyDenyRights(entry.Path, sid, new DenyRights(
                Read: entry.SavedRights.Read,
                Execute: entry.SavedRights.Execute));
        }
        else
        {
            _aclService.ApplyAllowRights(entry.Path, sid, new AllowRights(
                Execute: entry.SavedRights.Execute,
                Write: entry.SavedRights.Write,
                Special: entry.SavedRights.Special));
        }

        if (!isContainer && entry.SavedRights.Own)
        {
            bool isDirectory = Directory.Exists(entry.Path);
            _ntfsOps.ApplyOwnerChange(entry, CheckState.Checked, isDirectory);
        }
    }

    private void ApplyModification(GrantedPathEntry entry, string sid, bool isContainer)
    {
        if (entry.SavedRights == null)
        {
            _ntfsOps.ApplyInitialGrant(entry);
            return;
        }

        var state = _grantsHelper.ReadRightsForEntry(entry);

        bool noAce = entry.IsDeny ? state.DirectDenyAceCount == 0 : state.DirectAllowAceCount == 0;
        bool hasDuplicates = entry.IsDeny
            ? state.DirectDenyAceCount > 1
            : state.DirectAllowAceCount > 1;

        bool rightsDiffer = RightsDiffer(entry, state);
        bool ownDiffers = !isContainer && OwnerDiffers(entry, state);

        if (noAce || hasDuplicates || rightsDiffer)
        {
            _aclService.RevertGrant(entry.Path, sid, entry.IsDeny);
            ApplyGrantEntry(entry, sid, isContainer);
            return;
        }

        if (ownDiffers)
        {
            bool isDirectory = Directory.Exists(entry.Path);
            _ntfsOps.ApplyOwnerChange(entry,
                entry.SavedRights.Own ? CheckState.Checked : CheckState.Unchecked,
                isDirectory);
        }
    }

    private static bool RightsDiffer(GrantedPathEntry entry, GrantRightsState state)
    {
        if (entry.SavedRights == null)
            return true;
        var saved = entry.SavedRights;

        if (!entry.IsDeny)
        {
            return saved.Execute != (state.AllowExecute == CheckState.Checked)
                   || saved.Write != (state.AllowWrite == CheckState.Checked)
                   || saved.Special != (state.AllowSpecial == CheckState.Checked);
        }

        return saved.Execute != (state.DenyExecute == CheckState.Checked)
               || saved.Read != (state.DenyRead == CheckState.Checked);
    }

    private static bool OwnerDiffers(GrantedPathEntry entry, GrantRightsState state)
    {
        if (entry.SavedRights == null)
            return false;

        if (!entry.IsDeny)
        {
            bool actualOwner = state.IsAccountOwner == CheckState.Checked;
            return entry.SavedRights.Own != actualOwner;
        }

        if (!entry.SavedRights.Own)
            return false;
        return state.IsAccountOwner == CheckState.Checked;
    }

    public static void ReportProgress(IWin32Window owner, ToolStripProgressBar bar, int c, int t)
    {
        if (owner is Control { IsDisposed: false } ctrl)
            ctrl.BeginInvoke(() => { bar.Value = Math.Min(c, t); });
    }
}

/// <summary>Result of Phase 1 NTFS removes.</summary>
public record NtfsRemoveResult(
    HashSet<(string Path, bool IsDeny)> SuccessfulRemoves,
    HashSet<string> SuccessfulTraverseRemoves,
    int CurrentProgress);

/// <summary>Result of Phase 3 NTFS applies — traverse path tracking data deferred to UI thread.</summary>
public record NtfsApplyResult(
    List<(GrantedPathEntry Entry, List<string> VisitedPaths)> TraverseAddVisitedPaths,
    List<(GrantedPathEntry Entry, List<string> VisitedPaths)> TraverseNtfsAppliedForModifications);