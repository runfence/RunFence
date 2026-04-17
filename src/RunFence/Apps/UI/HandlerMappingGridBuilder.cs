using RunFence.Account;
using RunFence.Apps;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Builds the list of row data items to populate the handler mappings grid.
/// Resolves account display names via the SID name cache.
/// </summary>
public class HandlerMappingGridBuilder(
    IHandlerMappingService handlerMappingService,
    ISidNameCacheService sidNameCache)
{
    /// <summary>
    /// Returns the row data items representing all current handler mappings in the database.
    /// </summary>
    public IReadOnlyList<HandlerMappingRowData> GetGridRows(AppDatabase database)
    {
        var result = new List<HandlerMappingRowData>();

        var all = handlerMappingService.GetAllHandlerMappings(database);
        foreach (var kvp in all.OrderBy(k => k.Key))
        {
            foreach (var entry in kvp.Value)
            {
                var app = database.Apps.FirstOrDefault(a => a.Id == entry.AppId);
                var appName = app?.Name ?? $"(unknown: {entry.AppId})";
                var accountName = GetAccountDisplayName(app);
                result.Add(new HandlerMappingRowData(
                    Key: kvp.Key,
                    HandlerDisplay: appName,
                    AccountDisplay: accountName,
                    ArgsTemplate: entry.ArgumentsTemplate ?? "",
                    Tag: new AppMappingRowTag(kvp.Key, entry.AppId)));
            }
        }

        var directMappings = handlerMappingService.GetEffectiveDirectHandlerMappings(database);
        foreach (var kvp in directMappings.OrderBy(k => k.Key))
        {
            var handlerDisplay = BuildDirectHandlerDisplay(kvp.Value);
            result.Add(new HandlerMappingRowData(
                Key: kvp.Key,
                HandlerDisplay: handlerDisplay,
                AccountDisplay: "(direct)",
                ArgsTemplate: "",
                Tag: new DirectHandlerRowTag(kvp.Key)));
        }

        return result;
    }

    /// <summary>
    /// Builds a display string for a direct handler entry (class name or truncated command).
    /// </summary>
    public static string BuildDirectHandlerDisplay(DirectHandlerEntry entry)
    {
        if (entry.ClassName != null)
            return entry.ClassName;
        if (entry.Command == null)
            return string.Empty;
        const int maxLen = 60;
        return entry.Command.Length > maxLen ? entry.Command[..maxLen] + "…" : entry.Command;
    }

    /// <summary>
    /// Returns the display name for the account owning an app entry.
    /// </summary>
    public string GetAccountDisplayName(AppEntry? app)
    {
        if (app == null) return "";
        if (!string.IsNullOrEmpty(app.AccountSid))
            return sidNameCache.GetDisplayName(app.AccountSid) ?? app.AccountSid;
        if (!string.IsNullOrEmpty(app.AppContainerName))
            return app.AppContainerName;
        return "";
    }
}
