using System.Windows.Forms;

namespace RunFence.Startup;

public class MessageBoxStartupRepairWarningPresenter : IStartupRepairWarningPresenter
{
    public void ShowStartupRepairWarning(string message)
        => MessageBox.Show(
            message,
            "RunFence - Startup Repair Warning",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
}
