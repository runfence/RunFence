using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Persistence.UI;
using RunFence.Startup;
using RunFence.Startup.UI;
using RunFence.Startup.UI.Forms;
using RunFence.UI.Forms;

namespace RunFence.UI;

/// <summary>
/// Runs startup security checks and shows the nag dialog after they complete.
/// Also handles manual enforcement runs triggered from the applications panel.
/// Extracted from <see cref="MainForm"/> so the form class is not responsible for these concerns.
/// </summary>
public class MainFormStartupOrchestrator(
    IStartupSecurityService startupSecurityService,
    ILoggingService log,
    ConfigManagementOrchestrator configHandler,
    IStartupEnforcementService enforcementService,
    IAppContainerService appContainerService,
    ApplicationState applicationState,
    SessionContext session)
{
    public async Task RunStartupChecksAsync(
        MainForm owner,
        bool suppressVisibility,
        Action showNagIfNeeded)
    {
        log.Info("MainFormStartupOrchestrator: running startup security checks.");
        applicationState.EnforcementGuard.Begin();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var findings = await Task.Run(() => startupSecurityService.RunChecks(cts.Token));
            if (owner.IsDisposed)
                return;

            var settings = session.Database.Settings;

            // Disk root ACL findings are tracked per-entry (suppress once seen); all others use a hash.
            var diskRootFindings = findings.Where(f => f.Category == StartupSecurityCategory.DiskRootAcl).ToList();
            var otherFindings = findings.Where(f => f.Category != StartupSecurityCategory.DiskRootAcl).ToList();

            var newOtherHash = StartupSecurityFinding.ComputeHash(otherFindings);
            var savedHash = settings.LastSecurityFindingsHash;

            var newDiskRootKeys = diskRootFindings
                .Select(f => f.ComputeKey())
                .Where(k => !settings.SeenDiskRootAclKeys.Contains(k))
                .ToList();

            bool shouldShowDialog = (otherFindings.Count > 0 && newOtherHash != savedHash)
                                    || newDiskRootKeys.Count > 0;

            if (shouldShowDialog)
            {
                foreach (var f in findings)
                    log.Warn($"Startup security: [{f.Category}] {f.TargetDescription} — {f.VulnerablePrincipal}: {f.AccessDescription}");

                using var dlg = new StartupSecurityDialog(findings);
                dlg.ShowDialog(owner);
            }

            var isUacUnsafe = SidResolutionHelper.IsCurrentUserInteractive();
            var shouldShowUacWarning = isUacUnsafe && !settings.UacSameAccountWarningSuppressed;
            if (shouldShowUacWarning)
                SecurityCheckRunner.ShowUacWarning(owner);

            bool settingsChanged = newOtherHash != savedHash;
            if (settingsChanged)
                settings.LastSecurityFindingsHash = newOtherHash;

            // Record all current disk root findings as seen (append-only; never removed)
            foreach (var key in diskRootFindings.Select(f => f.ComputeKey()))
            {
                if (settings.SeenDiskRootAclKeys.Contains(key))
                    continue;
                settings.SeenDiskRootAclKeys.Add(key);
                settingsChanged = true;
            }

            if (shouldShowUacWarning)
            {
                settings.UacSameAccountWarningSuppressed = true;
                settingsChanged = true;
            }
            else if (!isUacUnsafe && settings.UacSameAccountWarningSuppressed)
            {
                // Condition became safe — reset so warning reappears if it becomes unsafe again
                settings.UacSameAccountWarningSuppressed = false;
                settingsChanged = true;
            }

            if (settingsChanged)
                configHandler.SaveSecurityFindingsHash();

            log.Info($"MainFormStartupOrchestrator: startup security checks complete ({findings.Count} finding(s)).");
        }
        catch (OperationCanceledException)
        {
            log.Warn("Startup security check timed out");
        }
        catch (Exception ex)
        {
            log.Error("Startup security check failed", ex);
        }
        finally
        {
            if (!owner.IsDisposed)
                applicationState.EnforcementGuard.End();
        }

        // Show nag dialog after security checks — runs after enforcement guard is released to avoid dialog stacking.
        // Skipped when suppressing initial visibility (background mode); deferred until TryShowWindow is called.
        if (!owner.IsDisposed && !suppressVisibility)
        {
            showNagIfNeeded();
            if (!owner.IsDisposed)
                NativeInterop.ForceToForeground(owner);
        }
    }

    public async void RunEnforcement(Form owner, Control guardOwner)
    {
        if (applicationState.EnforcementGuard.IsInProgress)
            return;

        applicationState.EnforcementGuard.Begin(guardOwner);
        try
        {
            var database = session.Database;
            var snapshot = database.CreateSnapshot();

            var result = await Task.Run(() => enforcementService.Enforce(snapshot));

            if (!owner.IsDisposed)
            {
                foreach (var (appId, timestamp) in result.TimestampUpdates)
                {
                    var app = database.Apps.FirstOrDefault(a => a.Id == appId);
                    if (app != null)
                        app.LastKnownExeTimestamp = timestamp;
                }

                // Re-track traverse grants on the live database. NTFS ACLs were applied by Enforce() on
                // the snapshot; we write DB entries directly because EnsureTraverseAccess would skip
                // tracking when ACEs already exist (anyAceAdded = false). AppliedPaths from
                // Enforce() are passed so AllAppliedPaths is set correctly for precise future reverts.
                bool traverseRetracked = false;
                foreach (var (container, traverseDir, appliedPaths) in result.TraverseGrants)
                {
                    var containerSid = appContainerService.GetSid(container.Name);
                    var traversePaths = TraversePathsHelper.GetOrCreateTraversePaths(database, containerSid);
                    traverseRetracked |= TraversePathsHelper.TrackPath(traversePaths, traverseDir, appliedPaths);
                }

                if (result.TimestampUpdates.Count > 0 || traverseRetracked)
                    configHandler.SaveConfigAfterEnforcement(database);
            }

            if (!owner.IsDisposed)
                MessageBox.Show("ACLs and shortcuts reapplied successfully.", "Reapply", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            log.Error("Manual enforcement failed", ex);
            if (!owner.IsDisposed)
                MessageBox.Show($"Enforcement failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            applicationState.EnforcementGuard.End(guardOwner);
        }
    }
}