using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.DragBridge.UI.Forms;

/// <summary>
/// Prompts the user about inaccessible files by showing <see cref="DragBridgeAccessDialog"/>.
/// Implements <see cref="IDragBridgeAccessPrompt"/> to decouple DragBridgePasteHandler from WinForms.
/// </summary>
public class DragBridgeAccessPrompt(IModalTracker modalTracker) : IDragBridgeAccessPrompt
{
    public DragBridgeAccessAction? Ask(string targetDisplayName, IReadOnlyList<string> inaccessiblePaths, long totalSizeBytes)
    {
        modalTracker.BeginModal();
        try
        {
            using var dlg = new DragBridgeAccessDialog(targetDisplayName, inaccessiblePaths, totalSizeBytes);
            dlg.Shown += (_, _) => { WindowForegroundHelper.ForceToForeground(dlg.Handle); dlg.BringToFront(); };
            dlg.ShowDialog();
            return dlg.ChosenAction;
        }
        finally
        {
            modalTracker.EndModal();
        }
    }
}