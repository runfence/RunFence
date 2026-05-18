using RunFence.Core.Models;

namespace RunFence.Persistence;

public sealed record MainConfigImportRepairPlan(
    List<AppEntry> AdditionalApps,
    List<MainConfigAdditionalAppIdRename> AdditionalAppIdRenames,
    List<string> OrphanedGrantSids);
