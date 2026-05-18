using RunFence.Core.Models;

namespace RunFence.Apps;

public interface IAssociationPolicyService
{
    bool IsDefaultBrowser(string appId, IReadOnlyDictionary<string, IReadOnlyList<HandlerMappingEntry>> allMappings);
    void ResolveConflictsForSid(
        string sid,
        Dictionary<string, HandlerMappingEntry> appMappings,
        Dictionary<string, DirectHandlerEntry> directMappings,
        AppDatabase database);
}
