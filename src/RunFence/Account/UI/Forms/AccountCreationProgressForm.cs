namespace RunFence.Account.UI.Forms;

public partial class AccountCreationProgressForm : Form
{
    public void SetStatus(string message) => _statusLabel.Text = message;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
            e.Cancel = true;
        else
            base.OnFormClosing(e);
    }
}