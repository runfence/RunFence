using System.Windows.Forms;

namespace RunFence.Startup;

public class MessageBoxStartupEnforcementMessagePresenter : IStartupEnforcementMessagePresenter
{
    public void ShowRepairSaveFailure(string message)
    {
        MessageBox.Show(
            $"RunFence repaired missing application paths before reapply, but saving those repairs failed:\n\n{message}",
            "Reapply Repair Warning",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    public void ShowSuccess() =>
        MessageBox.Show(
            "ACLs and shortcuts reapplied successfully.",
            "Reapply",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

    public void ShowShortcutWarning(string warningMessage)
    {
        MessageBox.Show(
            $"ACLs were reapplied, but shortcut enforcement reported warnings:\n\n{warningMessage}",
            "Reapply Warning",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    public void ShowEnforcementFailure(Exception exception) =>
        MessageBox.Show(
            $"Enforcement failed: {exception.Message}",
            "Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
}
