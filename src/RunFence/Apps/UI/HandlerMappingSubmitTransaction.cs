using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

public class HandlerMappingSubmitTransaction(
    IHandlerMappingService handlerMappingService,
    HandlerMappingSyncService syncService)
{
    public HandlerMappingSubmitResult Submit(
        IHandlerMappingDialogPersistence persistence,
        IReadOnlyList<string> submittedKeys,
        IReadOnlyList<string> affectedAppIds,
        Func<AppDatabase, IReadOnlyList<string>?> mutate)
    {
        AppDatabase? database = null;
        Dictionary<string, KeyStateSnapshot>? keySnapshots = null;
        Dictionary<string, AppStateSnapshot>? appSnapshots = null;

        try
        {
            database = persistence.GetDatabase();
            keySnapshots = CaptureKeySnapshots(database, submittedKeys);
            appSnapshots = CaptureAppSnapshots(database, affectedAppIds);

            var keysToRestore = mutate(database);
            persistence.SaveDatabase();

            var syncResult = syncService.Sync(keysToRestore);
            return syncResult.Succeeded
                ? new HandlerMappingSubmitResult(ShouldClose: true, SavedDurably: true)
                : new HandlerMappingSubmitResult(
                    ShouldClose: true,
                    SavedDurably: true,
                    RegistrySyncWarning: syncResult.WarningMessage);
        }
        catch (Exception ex)
        {
            if (database != null && keySnapshots != null && appSnapshots != null)
                RestoreBackingState(database, keySnapshots, appSnapshots);

            return new HandlerMappingSubmitResult(
                ShouldClose: false,
                SavedDurably: false,
                SaveError: ex.Message);
        }
    }

    private Dictionary<string, KeyStateSnapshot> CaptureKeySnapshots(AppDatabase database, IReadOnlyList<string> submittedKeys)
    {
        var allMappings = handlerMappingService.GetAllHandlerMappings(database);
        var directMappings = handlerMappingService.GetEffectiveDirectHandlerMappings(database);
        var snapshots = new Dictionary<string, KeyStateSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in submittedKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var appMappings = allMappings.TryGetValue(key, out var mappings)
                ? mappings.Select(CloneHandlerMapping).ToList()
                : [];
            var directSnapshot = directMappings.TryGetValue(key, out var directEntry)
                ? DirectHandlerSnapshot.Present(CloneDirectHandler(directEntry))
                : DirectHandlerSnapshot.Absent;

            snapshots[key] = new KeyStateSnapshot(appMappings, directSnapshot);
        }

        return snapshots;
    }

    private static Dictionary<string, AppStateSnapshot> CaptureAppSnapshots(AppDatabase database, IReadOnlyList<string> affectedAppIds)
    {
        var snapshots = new Dictionary<string, AppStateSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var appId in affectedAppIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var app = database.Apps.FirstOrDefault(existing =>
                string.Equals(existing.Id, appId, StringComparison.OrdinalIgnoreCase));
            if (app == null)
                continue;

            snapshots[appId] = new AppStateSnapshot(
                app.PathPrefixes?.ToList(),
                app.AllowPassingArguments);
        }

        return snapshots;
    }

    private void RestoreBackingState(
        AppDatabase database,
        IReadOnlyDictionary<string, KeyStateSnapshot> keySnapshots,
        IReadOnlyDictionary<string, AppStateSnapshot> appSnapshots)
    {
        foreach (var (key, snapshot) in keySnapshots)
        {
            RemoveCurrentMappingsForKey(database, key);

            foreach (var appMapping in snapshot.AppMappings)
                handlerMappingService.SetHandlerMapping(key, CloneHandlerMapping(appMapping), database);

            if (snapshot.DirectHandler.Exists && snapshot.DirectHandler.Entry != null)
            {
                var directEntry = snapshot.DirectHandler.Entry
                    ?? throw new InvalidOperationException("Direct handler snapshot entry was unexpectedly null.");
                handlerMappingService.SetDirectHandlerMapping(
                    key,
                    CloneDirectHandler(directEntry),
                    database);
            }
        }

        foreach (var (appId, snapshot) in appSnapshots)
        {
            var app = database.Apps.FirstOrDefault(existing =>
                string.Equals(existing.Id, appId, StringComparison.OrdinalIgnoreCase));
            if (app == null)
                continue;

            app.PathPrefixes = snapshot.PathPrefixes?.ToList();
            app.AllowPassingArguments = snapshot.AllowPassingArguments;
        }
    }

    private void RemoveCurrentMappingsForKey(AppDatabase database, string key)
    {
        var currentMappings = handlerMappingService.GetAllHandlerMappings(database);
        if (currentMappings.TryGetValue(key, out var appMappings))
        {
            foreach (var appMapping in appMappings)
                handlerMappingService.RemoveHandlerMapping(key, appMapping.AppId, database);
        }

        var directMappings = handlerMappingService.GetEffectiveDirectHandlerMappings(database);
        if (directMappings.ContainsKey(key))
            handlerMappingService.RemoveDirectHandlerMapping(key, database);
    }

    private static HandlerMappingEntry CloneHandlerMapping(HandlerMappingEntry entry)
        => new(
            entry.AppId,
            entry.ArgumentsTemplate,
            entry.PathPrefixes?.ToList(),
            entry.ReplacePrefixes);

    private static DirectHandlerEntry CloneDirectHandler(DirectHandlerEntry entry)
        => new()
        {
            ClassName = entry.ClassName,
            Command = entry.Command
        };

    private sealed record KeyStateSnapshot(
        IReadOnlyList<HandlerMappingEntry> AppMappings,
        DirectHandlerSnapshot DirectHandler);

    private sealed record AppStateSnapshot(
        IReadOnlyList<string>? PathPrefixes,
        bool AllowPassingArguments);

    private sealed record DirectHandlerSnapshot(bool Exists, DirectHandlerEntry? Entry)
    {
        public static readonly DirectHandlerSnapshot Absent = new(false, null);

        public static DirectHandlerSnapshot Present(DirectHandlerEntry entry) => new(true, entry);
    }
}
