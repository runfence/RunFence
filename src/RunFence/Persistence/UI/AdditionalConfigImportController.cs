using RunFence.Persistence;

namespace RunFence.Persistence.UI;

public class AdditionalConfigImportController(
    AdditionalConfigImportCoordinator coordinator)
{
    public AdditionalConfigImportPresentationResult Import(string importJsonPath, string configPath)
    {
        var result = coordinator.ImportAdditionalConfig(importJsonPath, configPath);
        return new AdditionalConfigImportPresentationResult(
            result.Status == AdditionalConfigImportStatus.Succeeded,
            result.Errors,
            result.Status);
    }
}

public sealed record AdditionalConfigImportPresentationResult(
    bool Succeeded,
    IReadOnlyList<string> Errors,
    AdditionalConfigImportStatus Status);
