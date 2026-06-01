using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Persistence;

namespace RunFence.Account.UI.AppContainer;

/// <summary>
/// Handles the business logic for creating and editing AppContainer entries:
/// applying edits to an existing entry or creating a new one, including
/// capability changes, loopback exemption, and COM access updates.
/// </summary>
public class AppContainerEditService(
    IAppContainerService appContainerService,
    IDatabaseProvider databaseProvider,
    ILoggingService log,
    IMainConfigPersistence mainConfigPersistence,
    ISessionProvider sessionProvider) : IAppContainerEditService
{
    private void SaveConfig(AppDatabase database)
    {
        var session = sessionProvider.GetSession();
        mainConfigPersistence.SaveConfig(
            database,
            session.PinDerivedKey,
            session.CredentialStore.ArgonSalt);
    }

    /// <summary>
    /// Applies edit changes to an existing AppContainer entry (mutates it in place).
    /// Returns a result describing what happened; the caller is responsible for showing messages.
    /// </summary>
    public async Task<AppContainerEditResult> ApplyEditChanges(
        AppContainerEntry existing,
        string displayName,
        List<string> capabilities,
        bool loopback,
        List<string> newComClsids,
        bool isEphemeral)
    {
        var originalDisplayName = existing.DisplayName;
        var originalCapabilities = existing.Capabilities?.ToList();
        var originalIsEphemeral = existing.IsEphemeral;
        var originalDeleteAfterUtc = existing.DeleteAfterUtc;
        var originalEnableLoopback = existing.EnableLoopback;
        var originalComAccessClsids = existing.ComAccessClsids?.ToList();
        var oldCapabilities = existing.Capabilities ?? [];
        var capabilitiesChanged = !oldCapabilities.OrderBy(x => x)
            .SequenceEqual(capabilities.OrderBy(x => x), StringComparer.OrdinalIgnoreCase);
        var oldComClsids = existing.ComAccessClsids ?? [];
        var comToRemove = oldComClsids.Except(newComClsids, StringComparer.OrdinalIgnoreCase).ToList();
        var comToAdd = newComClsids.Except(oldComClsids, StringComparer.OrdinalIgnoreCase).ToList();
        var disablingLoopback = existing.EnableLoopback && !loopback;
        var enablingLoopback = !existing.EnableLoopback && loopback;
        var warnings = new List<string>();
        var removalWarnings = new List<string>();
        string? containerSid = existing.Sid;

        if (disablingLoopback)
        {
            var removed = await appContainerService.SetLoopbackExemption(existing.Name, false);
            if (!removed)
            {
                return new AppContainerEditResult(
                    AppContainerOperationStatus.CleanupPending,
                    existing,
                    capabilitiesChanged,
                    "Failed to disable loopback exemption.",
                    ["Loopback exemption"]);
            }
        }

        if (comToRemove.Count > 0)
        {
            try
            {
                containerSid = appContainerService.GetSid(existing.Name);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to resolve container SID for '{existing.Name}'", ex);
                return new AppContainerEditResult(
                    AppContainerOperationStatus.CleanupPending,
                    existing,
                    capabilitiesChanged,
                    $"Container SID lookup failed: {ex.Message}",
                    ["COM access"]);
            }

            foreach (var clsid in comToRemove)
            {
                var revokeResult = appContainerService.RevokeComAccess(containerSid, clsid);
                if (!revokeResult.Succeeded)
                {
                    var message = revokeResult.ErrorMessage ?? "Unknown revoke failure.";
                    log.Warn($"Failed to revoke COM access for CLSID {clsid}: {message}");
                    removalWarnings.Add($"Revoke {clsid}: {message}");
                }
            }

            if (removalWarnings.Count > 0)
            {
                return new AppContainerEditResult(
                    AppContainerOperationStatus.CleanupPending,
                    existing,
                    capabilitiesChanged,
                    "Some AppContainer cleanup operations failed.",
                    removalWarnings);
            }
        }

        existing.DisplayName = displayName;
        existing.Capabilities = capabilities.Count > 0 ? capabilities : null;
        existing.IsEphemeral = isEphemeral;
        existing.DeleteAfterUtc = isEphemeral ? existing.DeleteAfterUtc ?? DateTime.UtcNow.AddHours(24) : null;
        if (!isEphemeral)
            existing.DeleteAfterUtc = null;
        existing.EnableLoopback = loopback;
        existing.ComAccessClsids = newComClsids.Count > 0 ? [..newComClsids] : null;

        try
        {
            SaveConfig(databaseProvider.GetDatabase());
        }
        catch (Exception ex)
        {
            log.Error($"Failed to save AppContainer edit for '{existing.Name}'", ex);
            var restoreResult = disablingLoopback || comToRemove.Count > 0
                ? AppContainerOperationStatus.SaveFailedAfterOs
                : AppContainerOperationStatus.SaveFailedBeforeOs;
            if (restoreResult == AppContainerOperationStatus.SaveFailedBeforeOs)
            {
                existing.DisplayName = originalDisplayName;
                existing.Capabilities = originalCapabilities;
                existing.IsEphemeral = originalIsEphemeral;
                existing.DeleteAfterUtc = originalDeleteAfterUtc;
                existing.EnableLoopback = originalEnableLoopback;
                existing.ComAccessClsids = originalComAccessClsids;
            }

            return new AppContainerEditResult(
                restoreResult,
                existing,
                capabilitiesChanged,
                ex.Message,
                []);
        }

        if (comToAdd.Count > 0 || enablingLoopback)
        {
            try
            {
                containerSid ??= appContainerService.GetSid(existing.Name);
                existing.Sid = containerSid;
            }
            catch (Exception ex)
            {
                log.Error($"Failed to resolve container SID for '{existing.Name}'", ex);
                return new AppContainerEditResult(
                    AppContainerOperationStatus.SystemFailed,
                    existing,
                    capabilitiesChanged,
                    $"Container SID lookup failed: {ex.Message}",
                    ["COM access"]);
            }
        }

        if (enablingLoopback)
        {
            var enabled = await appContainerService.SetLoopbackExemption(existing.Name, true);
            if (!enabled)
            {
                return new AppContainerEditResult(
                    AppContainerOperationStatus.SystemFailed,
                    existing,
                    capabilitiesChanged,
                    "Failed to enable loopback exemption.",
                    ["Loopback exemption"]);
            }
        }

        foreach (var clsid in comToAdd)
        {
            var grantResult = appContainerService.GrantComAccess(containerSid!, clsid);
            if (!grantResult.Succeeded)
            {
                var message = grantResult.ErrorMessage ?? "Unknown grant failure.";
                log.Warn($"Failed to grant COM access for CLSID {clsid}: {message}");
                warnings.Add($"Grant {clsid}: {message}");
            }
        }

        var status = warnings.Count > 0
            ? AppContainerOperationStatus.SystemFailed
            : AppContainerOperationStatus.Succeeded;
        return new AppContainerEditResult(status, existing, capabilitiesChanged, null, warnings);
    }

    /// <summary>
    /// Creates a new AppContainer entry, registers it in the database, and sets
    /// <see cref="AppContainerEntry.ComAccessClsids"/> to the successfully granted CLSIDs.
    /// Returns an <see cref="AppContainerCreateResult"/> where <see cref="AppContainerCreateResult.Entry"/>
    /// is non-null when the container remains active in memory after the operation.
    /// </summary>
    public async Task<AppContainerCreateResult> CreateNewContainer(
        string profileName,
        string displayName,
        bool isEphemeral,
        List<string> capabilities,
        bool loopback,
        List<string> comClsids)
    {
        var database = databaseProvider.GetDatabase();
        if (database.AppContainers.Any(c =>
                string.Equals(c.Name, profileName, StringComparison.OrdinalIgnoreCase)))
            return new AppContainerCreateResult(
                AppContainerOperationStatus.SystemFailed,
                null,
                $"A container with profile name '{profileName}' already exists.",
                []);

        var entry = new AppContainerEntry
        {
            Name = profileName,
            DisplayName = displayName,
            Capabilities = capabilities.Count > 0 ? capabilities : null,
            EnableLoopback = loopback,
            IsEphemeral = isEphemeral,
            DeleteAfterUtc = isEphemeral ? DateTime.UtcNow.AddHours(24) : null,
            LifecycleState = "IntentSaved"
        };

        database.AppContainers.Add(entry);

        var loopbackApplied = false;
        var grantedComClsids = new List<string>();
        var osMutationStarted = false;

        try
        {
            SaveConfig(database);
        }
        catch (Exception ex)
        {
            database.AppContainers.Remove(entry);
            log.Error($"Failed to save AppContainer intent for '{profileName}'", ex);
            return new AppContainerCreateResult(
                AppContainerOperationStatus.SaveFailedBeforeOs,
                null,
                ex.Message,
                []);
        }

        try
        {
            osMutationStarted = true;
            var profileResult = appContainerService.CreateProfile(entry);
            if (profileResult.Status != AppContainerProfileSetupStatus.Succeeded)
                throw new InvalidOperationException(profileResult.ErrorMessage ?? $"Failed to create AppContainer profile '{entry.Name}'.");

            if (entry.EnableLoopback)
            {
                var loopbackOk = await appContainerService.SetLoopbackExemption(entry.Name, true);
                if (!loopbackOk)
                    throw new InvalidOperationException("Failed to enable loopback exemption.");
                loopbackApplied = true;
            }

            var sid = appContainerService.GetSid(entry.Name);
            entry.Sid = sid;
            foreach (var clsid in comClsids)
            {
                var grantResult = appContainerService.GrantComAccess(sid, clsid);
                if (!grantResult.Succeeded)
                    throw new InvalidOperationException(grantResult.ErrorMessage ?? $"Failed to grant COM access for '{clsid}'.");
                grantedComClsids.Add(clsid);
            }

            entry.ComAccessClsids = grantedComClsids.Count > 0 ? grantedComClsids : null;
            entry.LifecycleState = null;
            try
            {
                SaveConfig(database);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to save AppContainer '{profileName}' after OS setup", ex);
                return new AppContainerCreateResult(
                    AppContainerOperationStatus.SaveFailedAfterOs,
                    entry,
                    ex.Message,
                    []);
            }

            return new AppContainerCreateResult(
                AppContainerOperationStatus.Succeeded,
                entry,
                null,
                []);
        }
        catch (Exception ex)
        {
            var rollbackFailed = false;
            if (osMutationStarted)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(entry.Sid))
                    {
                        foreach (var clsid in grantedComClsids)
                        {
                            var revokeResult = appContainerService.RevokeComAccess(entry.Sid, clsid);
                            if (!revokeResult.Succeeded)
                                throw new InvalidOperationException(revokeResult.ErrorMessage ?? $"Failed to revoke COM access for '{clsid}'.");
                        }
                    }

                    if (loopbackApplied)
                    {
                        var loopbackReverted = await appContainerService.SetLoopbackExemption(entry.Name, false);
                        if (!loopbackReverted)
                            throw new InvalidOperationException($"Rollback failed to disable loopback for '{entry.Name}'.");
                    }

                    await appContainerService.DeleteProfile(entry.Name, hadLoopback: false);
                }
                catch (Exception rollbackEx)
                {
                    rollbackFailed = true;
                    log.Error($"Rollback failed for AppContainer '{profileName}'", rollbackEx);
                }
            }

            if (!rollbackFailed)
            {
                database.AppContainers.Remove(entry);
                if (osMutationStarted)
                {
                    try
                    {
                        SaveConfig(database);
                    }
                    catch (Exception saveEx)
                    {
                        rollbackFailed = true;
                        log.Error($"Failed to persist rollback cleanup for AppContainer '{profileName}'", saveEx);
                        database.AppContainers.Add(entry);
                    }
                }
            }

            if (rollbackFailed)
            {
                entry.LifecycleState = "CleanupPending";
                entry.DeleteAfterUtc ??= DateTime.UtcNow.AddHours(24);
                try
                {
                    SaveConfig(database);
                }
                catch (Exception saveEx)
                {
                    log.Error($"Failed to persist cleanup-pending state for AppContainer '{profileName}'", saveEx);
                }
            }

            log.Error($"Failed to create AppContainer '{profileName}'", ex);
            return new AppContainerCreateResult(
                rollbackFailed ? AppContainerOperationStatus.CleanupPending : AppContainerOperationStatus.SystemFailed,
                rollbackFailed ? entry : null,
                ex.Message,
                []);
        }
    }
}
