using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence.UI;
using RunFence.Startup;
using RunFence.Startup.UI;
using RunFence.Startup.UI.Forms;

namespace RunFence.UI;

/// <summary>
/// Runs startup security checks and shows the nag dialog after they complete.
/// Also handles the one-time first-run export prompt and manual enforcement runs
/// triggered from the applications panel.
/// Extracted from <see cref="MainForm"/> so the form class is not responsible for these concerns.
/// </summary>
public class MainFormStartupOrchestrator(
    IStartupSecurityService startupSecurityService,
    ILoggingService log,
    ConfigSaveOrchestrator configSaver,
    IStartupEnforcementService enforcementService,
    StartupEnforcementRunner startupEnforcementRunner,
    ApplicationState applicationState,
    SessionContext session,
    EnforcementResultApplier enforcementResultApplier,
    FindingLocationHelper findingLocationHelper,
    IMainFormFirstRunExporter firstRunExporter,
    IStartupEnforcementMessagePresenter messagePresenter)
{
    public async Task RunStartupChecksAsync(
        Form owner,
        bool suppressVisibility,
        Action showNagIfNeeded)
    {
        if (!suppressVisibility)
            await firstRunExporter.PromptExportSettingsIfNeededAsync(owner);

        if (owner.IsDisposed)
            return;

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

                using var dlg = new StartupSecurityDialog(findings, findingLocationHelper);
                await dlg.ShowDialogAsync(owner);
            }

            var isUacUnsafe = SidResolutionHelper.IsCurrentUserInteractive();
            var shouldShowUacWarning = isUacUnsafe && !settings.UacSameAccountWarningSuppressed && !DebugHelper.IsDebugBuild;
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
                configSaver.SaveSecurityFindingsHash();

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
        }
    }

    public async void RunEnforcement(Form owner, Control guardOwner)
    {
        if (applicationState.EnforcementGuard.IsInProgress)
            return;

        applicationState.EnforcementGuard.Begin(guardOwner);
        try
        {
            var preparation = PrepareEnforcementSnapshot();
            if (preparation.SaveFailureMessage != null)
            {
                if (!owner.IsDisposed)
                {
                    messagePresenter.ShowRepairSaveFailure(preparation.SaveFailureMessage);
                }
                return;
            }

            var database = session.Database;
            var snapshot = preparation.Snapshot!;

            var result = await Task.Run(() => enforcementService.Enforce(snapshot));

            if (!owner.IsDisposed)
            {
                var (timestampsChanged, traverseRetracked) = enforcementResultApplier.ApplyToDatabase(result, database);

                if (timestampsChanged || traverseRetracked)
                    configSaver.SaveConfigAfterEnforcement(database);
            }

            if (!owner.IsDisposed)
            {
                var warningMessage = result.Warnings is { Count: > 0 }
                    ? string.Join("\n\n", result.Warnings)
                    : null;
                if (warningMessage == null)
                {
                    messagePresenter.ShowSuccess();
                }
                else
                {
                    messagePresenter.ShowShortcutWarning(warningMessage);
                }
            }
        }
        catch (Exception ex)
        {
            log.Error("Manual enforcement failed", ex);
            if (!owner.IsDisposed)
                messagePresenter.ShowEnforcementFailure(ex);
        }
        finally
        {
            applicationState.EnforcementGuard.End(guardOwner);
        }
    }

    internal ManualEnforcementPreparationResult PrepareEnforcementSnapshot()
    {
        var repairResult = startupEnforcementRunner.RepairMissingAppEntryPaths();
        foreach (var warning in repairResult.Warnings)
            log.Warn($"Manual reapply app path repair warning: {warning}");

        if (repairResult.SaveFailureMessage != null)
            return new ManualEnforcementPreparationResult(null, repairResult.SaveFailureMessage);

        return new ManualEnforcementPreparationResult(session.Database.CreateSnapshot(), null);
    }
}

internal sealed record ManualEnforcementPreparationResult(
    AppDatabase? Snapshot,
    string? SaveFailureMessage);
