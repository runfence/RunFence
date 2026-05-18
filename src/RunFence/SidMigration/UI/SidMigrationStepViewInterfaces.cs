using RunFence.Core.Models;

namespace RunFence.SidMigration.UI;

public interface ISidMigrationStepView
{
    Control View { get; }
}

public interface ISidMigrationPathStepView : ISidMigrationStepView
{
    event EventHandler? SkipRequested;

    void RestoreState(List<(string path, bool isChecked)> savedState);

    List<string> CollectSelectedPaths();

    List<(string path, bool isChecked)>? SavedState { get; }
}

public interface ISidMigrationMappingStepView : ISidMigrationStepView
{
    void BeginAsync(Action onReady, Action onFailed);

    bool TryCollectSelections(out List<SidMigrationMapping> mappings, out List<string> deleteSids);
}

public interface ISidMigrationProgressStepView : ISidMigrationStepView
{
    ProgressBar ProgressBar { get; }

    Label StatusLabel { get; }

    Button CancelButton { get; }

    void Configure(string statusText, int? maxValue, bool showCancelButton);
}

public interface ISidMigrationDiskApplyStepView : ISidMigrationProgressStepView
{
    void SetCurrentPath(string currentPath);
}

public interface ISidMigrationInAppStepView : ISidMigrationStepView
{
    event EventHandler? MigrationApplied;
}
