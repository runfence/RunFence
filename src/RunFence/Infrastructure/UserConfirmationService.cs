namespace RunFence.Infrastructure;

public class UserConfirmationService : IUserConfirmationService
{
    public bool Confirm(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
}
