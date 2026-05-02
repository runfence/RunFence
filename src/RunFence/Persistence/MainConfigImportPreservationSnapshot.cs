using RunFence.Core.Models;

namespace RunFence.Persistence;

public class MainConfigImportPreservationSnapshot
{
    public Dictionary<string, List<GrantedPathEntry>> OldMainGrants { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<GrantedPathEntry>> AdditionalGrants { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<GrantedPathEntry> OldMainSharedContainerTraverseGrants { get; init; } = [];

    public List<GrantedPathEntry> AdditionalSharedContainerTraverseGrants { get; init; } = [];

    public List<AccountEntry> AccountsToPreserve { get; init; } = [];

    public Dictionary<string, string> OldSidNames { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<AppContainerEntry> OldContainers { get; init; } = [];
}
