using RunFence.Apps.UI;

namespace RunFence.Security.UI.Forms;

public partial class DisableBlockingDialog : Form
{
    public DisableBlockingChoice Choice { get; private set; } = DisableBlockingChoice.Cancelled;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Icon = AppIcons.GetAppIcon();
    }

    private void OnUntilRestartClick(object? sender, EventArgs e)
    {
        Choice = DisableBlockingChoice.UntilRestart;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnForTenMinutesClick(object? sender, EventArgs e)
    {
        Choice = DisableBlockingChoice.ForTenMinutes;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnPermanentlyClick(object? sender, EventArgs e)
    {
        Choice = DisableBlockingChoice.Permanently;
        DialogResult = DialogResult.OK;
        Close();
    }
}
