namespace RunFence.UI;

public interface IMainFormFirstRunExporter
{
    Task PromptExportSettingsIfNeededAsync(IWin32Window owner);
}
