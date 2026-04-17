using System.Text.Json;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Persistence;

/// <summary>Thrown when an import is blocked by evaluation-mode license limits.</summary>
public class EvaluationLimitException(string message) : Exception(message);

/// <summary>
/// Handles the business logic of importing app configs from JSON files.
/// Extracted from ConfigManagerSection to separate import concerns from UI event handling.
/// </summary>
public class ConfigImportHandler(
    IAppConfigService appConfigService,
    ILicenseService licenseService,
    ISessionProvider sessionProvider,
    IPathGrantService pathGrantService,
    IGrantConfigTracker grantTracker,
    ILoggingService log,
    IAppEntryIdGenerator idGenerator)
{
    /// <summary>
    /// Imports a JSON file as the main config. Validates license limits, merges apps/settings/accounts
    /// from the imported data into the current database, and saves.
    /// Throws on parse errors or license violations.
    /// </summary>
    public void ImportMainConfig(string path)
    {
        var json = File.ReadAllText(path);
        var session = sessionProvider.GetSession();
        var database = session.Database;

        var importedDb = JsonSerializer.Deserialize<AppDatabase>(json, JsonDefaults.Options)
                         ?? throw new InvalidOperationException("Failed to parse config file.");

        if (!licenseService.IsLicensed)
        {
            var violations = new List<string>();
            var additionalAppsCount = database.Apps.Count(a => appConfigService.GetConfigPath(a.Id) != null);
            var totalAfterImport = importedDb.Apps.Count + additionalAppsCount;
            var appsMsg = licenseService.GetRestrictionMessage(EvaluationFeature.Apps, totalAfterImport - 1);
            if (appsMsg != null)
                violations.Add($"Apps: {appsMsg}");
            var totalAllowlistEntries = importedDb.Accounts?.Sum(a => a.Firewall.Allowlist.Count) ?? 0;
            if (totalAllowlistEntries > Constants.EvaluationMaxFirewallAllowlistEntries)
                violations.Add(
                    $"Firewall whitelist: imported config has {totalAllowlistEntries} entries across all accounts (limit: {Constants.EvaluationMaxFirewallAllowlistEntries})");
            if (violations.Count > 0)
                throw new EvaluationLimitException(
                    "The imported config exceeds evaluation limits.\n\n" +
                    string.Join("\n", violations) + "\n\nActivate a license to remove these limits.");
        }

        var additionalApps = database.Apps
            .Where(a => appConfigService.GetConfigPath(a.Id) != null)
            .ToList();

        // Remove filesystem ACEs for main-config app SIDs that will no longer exist after import,
        // to avoid orphaned grants on disk for replaced apps. Only remove SIDs not present in either
        // the new imported config or additional configs (which share the live database).
        var incomingSids = importedDb.Apps
            .Where(a => !string.IsNullOrEmpty(a.AccountSid))
            .Select(a => a.AccountSid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additionalSids = additionalApps
            .Where(a => !string.IsNullOrEmpty(a.AccountSid))
            .Select(a => a.AccountSid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphanedSids = database.Apps
            .Where(a => appConfigService.GetConfigPath(a.Id) == null && !string.IsNullOrEmpty(a.AccountSid))
            .Select(a => a.AccountSid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(sid => !incomingSids.Contains(sid) && !additionalSids.Contains(sid))
            .ToList();
        foreach (var sid in orphanedSids)
            pathGrantService.RemoveAll(sid, updateFileSystem: true);

        // Repair ID collisions across ALL apps being imported (imported + additional) before merging.
        // First pass: assign unique IDs to any imported apps that collide with each other or additional apps.
        var allFinalIds = new HashSet<string>(additionalApps.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var app in importedDb.Apps)
        {
            if (!allFinalIds.Add(app.Id))
            {
                var newId = idGenerator.GenerateUniqueId(allFinalIds);
                log.Info($"ImportMainConfig: imported app '{app.Name}' had ID collision, regenerated: {app.Id} → {newId}");
                app.Id = newId;
                allFinalIds.Add(newId);
            }
        }

        // Second pass: repair additional apps that collide with the (already-repaired) imported apps.
        var importedIds = new HashSet<string>(importedDb.Apps.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var app in additionalApps)
        {
            if (importedIds.Contains(app.Id))
            {
                var newId = idGenerator.GenerateUniqueId(importedIds.Concat(additionalApps.Select(a => a.Id)));
                log.Info($"ImportMainConfig: additional app '{app.Name}' had ID collision, regenerated: {app.Id} → {newId}");
                app.Id = newId;
                importedIds.Add(newId);
            }
        }

        database.Apps.Clear();
        database.Apps.AddRange(importedDb.Apps);
        database.Apps.AddRange(additionalApps);

        database.Settings = importedDb.Settings ?? new AppSettings();

        foreach (var (sid, name) in importedDb.SidNames)
            database.SidNames.TryAdd(sid, name);

        foreach (var a in database.Accounts)
        {
            a.IsIpcCaller = false;
            a.Firewall = new FirewallAccountSettings();
            a.Grants.RemoveAll(g => grantTracker.IsInMainConfig(a.Sid, g));
        }

        foreach (var importedAccount in importedDb.Accounts ?? [])
        {
            if (importedAccount is { IsIpcCaller: false, Firewall.IsDefault: true } && importedAccount.Grants is not { Count: > 0 })
                continue;
            var entry = database.GetOrCreateAccount(importedAccount.Sid);
            entry.IsIpcCaller = importedAccount.IsIpcCaller;
            entry.Firewall = importedAccount.Firewall;
            foreach (var grant in importedAccount.Grants ?? [])
            {
                if (entry.Grants.Any(g =>
                        string.Equals(g.Path, grant.Path, StringComparison.OrdinalIgnoreCase) &&
                        g.IsDeny == grant.IsDeny && g.IsTraverseOnly == grant.IsTraverseOnly))
                    continue;
                entry.Grants.Add(grant);
            }
        }

        foreach (var a in database.Accounts.ToList())
            database.RemoveAccountIfEmpty(a.Sid);

        using var scope = session.PinDerivedKey.Unprotect();
        appConfigService.ReencryptAndSaveAll(session.CredentialStore, database, scope.Data);

        log.Info($"Main config imported from {path}");
    }

    /// <summary>
    /// Imports a JSON file into an existing additional config file, replacing its apps.
    /// </summary>
    public void ImportAdditionalConfig(string importJsonPath, string configPath)
    {
        var json = File.ReadAllText(importJsonPath);

        var importedConfig = JsonSerializer.Deserialize<AppConfig>(json, JsonDefaults.Options)
                             ?? throw new InvalidOperationException("Failed to parse config file.");

        var session = sessionProvider.GetSession();
        using var scope = session.PinDerivedKey.Unprotect();
        appConfigService.SaveImportedConfig(configPath, importedConfig.Apps, scope.Data,
            session.CredentialStore.ArgonSalt);

        log.Info($"Additional config imported from {importJsonPath} into {configPath}");
    }
}
