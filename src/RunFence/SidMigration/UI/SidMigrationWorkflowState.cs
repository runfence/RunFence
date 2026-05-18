using RunFence.Core.Models;

namespace RunFence.SidMigration.UI;

public sealed class SidMigrationWorkflowState
{
    public int CurrentStep { get; private set; }

    public List<(string path, bool isChecked)>? SavedPathState { get; set; }

    public List<string> RootPaths { get; set; } = [];

    public List<SidMigrationMapping> Mappings { get; set; } = [];

    public List<string> SidsToDelete { get; set; } = [];

    public List<OrphanedSid> OrphanedSids { get; set; } = [];

    public List<SidMigrationMatch> ScanResults { get; set; } = [];

    public (long applied, long errors) ApplyResult { get; set; }

    public bool InAppMigrationApplied { get; set; }

    public void SetCurrentStep(int step)
    {
        CurrentStep = step;
    }
}
