using RunFence.Core.Models;

namespace RunFence.SidMigration.UI;

public sealed record SidMigrationSelectionResult(
    bool Success,
    List<SidMigrationMapping> Mappings,
    List<string> DeleteSids,
    IReadOnlyDictionary<int, string> RowErrors);
