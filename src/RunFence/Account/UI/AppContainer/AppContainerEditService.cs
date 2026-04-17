using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.Persistence;

namespace RunFence.Account.UI.AppContainer;

/// <summary>
/// Result of <see cref="AppContainerEditService.ApplyEditChanges"/>.
/// </summary>
public record AppContainerEditResult(
    bool CapabilitiesChanged,
    bool LoopbackFailed,
    string? LoopbackFailAction,
    IReadOnlyList<string> ComErrors);

/// <summary>
/// Handles the business logic for creating and editing AppContainer entries:
/// applying edits to an existing entry or creating a new one, including
/// capability changes, loopback exemption, and COM access updates.
/// </summary>
public class AppContainerEditService(
    IAppContainerService appContainerService,
    IDatabaseProvider databaseProvider,
    ILoggingService log)
{
    /// <summary>
    /// Applies edit changes to an existing AppContainer entry (mutates it in place).
    /// Returns a result describing what happened; the caller is responsible for showing messages.
    /// </summary>
    public AppContainerEditResult ApplyEditChanges(
        AppContainerEntry existing,
        string displayName,
        List<string> capabilities,
        bool loopback,
        List<string> newComClsids,
        bool isEphemeral)
    {
        if (isEphemeral != existing.IsEphemeral)
        {
            existing.IsEphemeral = isEphemeral;
            existing.DeleteAfterUtc = isEphemeral ? DateTime.UtcNow.AddHours(24) : null;
        }

        var loopbackChanged = loopback != existing.EnableLoopback;

        var oldCapabilities = existing.Capabilities ?? [];
        var capabilitiesChanged = !oldCapabilities.OrderBy(x => x)
            .SequenceEqual(capabilities.OrderBy(x => x), StringComparer.OrdinalIgnoreCase);

        existing.DisplayName = displayName;
        existing.Capabilities = capabilities.Count > 0 ? capabilities : null;

        bool loopbackFailed = false;
        string? loopbackFailAction = null;
        if (loopbackChanged)
        {
            var succeeded = appContainerService.SetLoopbackExemption(existing.Name, loopback);
            if (succeeded)
                existing.EnableLoopback = loopback;
            else
            {
                loopbackFailed = true;
                loopbackFailAction = loopback ? "enable" : "disable";
            }
        }

        var succeededComClsids = new List<string>(existing.ComAccessClsids ?? []);
        var comErrors = new List<string>();
        try
        {
            var containerSid = appContainerService.GetSid(existing.Name);
            existing.Sid = containerSid;
            var oldCom = existing.ComAccessClsids ?? [];
            foreach (var clsid in newComClsids.Except(oldCom, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    appContainerService.GrantComAccess(containerSid, clsid);
                    succeededComClsids.Add(clsid);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to grant COM access for CLSID {clsid}: {ex.Message}");
                    comErrors.Add($"Grant {clsid}: {ex.Message}");
                }
            }

            foreach (var clsid in oldCom.Except(newComClsids, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    appContainerService.RevokeComAccess(containerSid, clsid);
                    succeededComClsids.Remove(clsid);
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to revoke COM access for CLSID {clsid}: {ex.Message}");
                    comErrors.Add($"Revoke {clsid}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to update COM access for container '{existing.Name}'", ex);
            comErrors.Add($"Container SID lookup failed: {ex.Message}");
        }

        existing.ComAccessClsids = succeededComClsids.Count > 0 ? succeededComClsids : null;

        return new AppContainerEditResult(capabilitiesChanged, loopbackFailed, loopbackFailAction, comErrors);
    }

    /// <summary>
    /// Creates a new AppContainer entry, registers it in the database, and sets
    /// <see cref="AppContainerEntry.ComAccessClsids"/> to the successfully granted CLSIDs.
    /// Returns the created entry on success, or null if creation failed (caller should not close dialog).
    /// <paramref name="validationError"/> is set for validation failures (show as warning);
    /// <paramref name="creationError"/> is set for OS-level creation failures (show as error).
    /// <paramref name="comErrors"/> holds partial COM access failures on success.
    /// </summary>
    public AppContainerEntry? CreateNewContainer(
        string profileName,
        string displayName,
        bool isEphemeral,
        List<string> capabilities,
        bool loopback,
        List<string> comClsids,
        out string? validationError,
        out string? creationError,
        out IReadOnlyList<string> comErrors)
    {
        validationError = null;
        creationError = null;
        comErrors = [];

        var database = databaseProvider.GetDatabase();
        if (database.AppContainers.Any(c =>
                string.Equals(c.Name, profileName, StringComparison.OrdinalIgnoreCase)))
        {
            validationError = $"A container with profile name '{profileName}' already exists.";
            return null;
        }

        var entry = new AppContainerEntry
        {
            Name = profileName,
            DisplayName = displayName,
            Capabilities = capabilities.Count > 0 ? capabilities : null,
            EnableLoopback = loopback,
            IsEphemeral = isEphemeral,
            DeleteAfterUtc = isEphemeral ? DateTime.UtcNow.AddHours(24) : null
        };

        try
        {
            appContainerService.CreateProfile(entry);

            if (entry.EnableLoopback)
            {
                var loopbackOk = appContainerService.SetLoopbackExemption(entry.Name, true);
                if (!loopbackOk)
                    entry.EnableLoopback = false;
            }

            var comGrantErrors = new List<string>();
            try
            {
                var sid = appContainerService.GetSid(entry.Name);
                entry.Sid = sid;
                var grantedClsids = new List<string>();
                foreach (var clsid in comClsids)
                {
                    try
                    {
                        appContainerService.GrantComAccess(sid, clsid);
                        grantedClsids.Add(clsid);
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"Failed to grant COM access for CLSID {clsid}: {ex.Message}");
                        comGrantErrors.Add($"Grant {clsid}: {ex.Message}");
                    }
                }

                entry.ComAccessClsids = grantedClsids.Count > 0 ? grantedClsids : null;
            }
            catch (Exception ex)
            {
                log.Error($"Failed to resolve container SID for '{entry.Name}'", ex);
                comGrantErrors.Add($"Container SID lookup failed: {ex.Message}");
                entry.ComAccessClsids = null;
            }

            comErrors = comGrantErrors;
            database.AppContainers.Add(entry);
            return entry;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to create AppContainer '{profileName}'", ex);
            creationError = ex.Message;
            return null;
        }
    }
}