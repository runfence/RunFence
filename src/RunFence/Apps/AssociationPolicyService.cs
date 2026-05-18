using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Ipc;

namespace RunFence.Apps;

public sealed class AssociationPolicyService(IIpcCallerAuthorizer callerAuthorizer) : IAssociationPolicyService
{
    public bool IsDefaultBrowser(string appId, IReadOnlyDictionary<string, IReadOnlyList<HandlerMappingEntry>> allMappings)
        => EvaluationConstants.BrowserAssociations.All(key =>
            allMappings.TryGetValue(key, out var entries) &&
            entries.Any(e => string.Equals(e.AppId, appId, StringComparison.Ordinal)));

    public void ResolveConflictsForSid(
        string sid,
        Dictionary<string, HandlerMappingEntry> appMappings,
        Dictionary<string, DirectHandlerEntry> directMappings,
        AppDatabase database)
    {
        foreach (var key in appMappings.Keys.Where(directMappings.ContainsKey).ToList())
        {
            var app = database.Apps.FirstOrDefault(a =>
                string.Equals(a.Id, appMappings[key].AppId, StringComparison.OrdinalIgnoreCase));
            if (app != null && callerAuthorizer.HasExplicitPerAppAuthorization(sid, app, database))
                directMappings.Remove(key);
            else
                appMappings.Remove(key);
        }
    }
}
