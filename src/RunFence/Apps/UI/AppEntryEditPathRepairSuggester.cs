using RunFence.Core.Models;
using RunFence.Launch;

namespace RunFence.Apps.UI;

public sealed class AppEntryEditPathRepairSuggester(
    VersionedPathRepairer repairer,
    VersionedPathAutoRepairTrustPolicy trustPolicy,
    VersionedPathRepairOptionsBuilder optionsBuilder,
    IMessageBoxService messageBoxService)
{
    public bool SuggestIfNeeded(AppEntry existingApp, IAppEditBrowseResultReceiver receiver)
    {
        if (existingApp.IsUrlScheme)
            return false;

        var repair = repairer.TryRepair(
            existingApp.ExePath,
            existingApp.IsFolder,
            optionsBuilder.ForEditSuggestion(existingApp));
        if (repair == null)
            return false;

        if (trustPolicy.TryCreateAutoRepairTrust(existingApp, out _))
            return false;

        var message =
            $"RunFence found a possible replacement for the missing app path.{Environment.NewLine}{Environment.NewLine}" +
            $"Current path:{Environment.NewLine}{existingApp.ExePath}{Environment.NewLine}{Environment.NewLine}" +
            $"Suggested path:{Environment.NewLine}{repair.Value.RepairedPath}{Environment.NewLine}{Environment.NewLine}" +
            "Choose Yes to update only the edit fields. The change will not be saved until you click Apply or OK.";
        if (messageBoxService.Show(
                message,
                "RunFence",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return false;
        }

        receiver.SetFilePath(repair.Value.RepairedPath);
        receiver.SetFolderMode(existingApp.IsFolder);
        return true;
    }
}
