namespace RunFence.UI.Forms;

public partial class ImportProgressForm : Form
{
    public ImportProgressForm(IWin32Window? owner)
    {
        InitializeComponent();
        _logTextBox.Font = new Font("Consolas", 9f);
        _okButton.Click += (_, _) => Close();
        FormClosed += (_, _) => Dispose();
        if (owner != null)
            Show(owner);
        else
            Show();
    }

    public void AppendLog(string text)
    {
        if (IsDisposed)
            return;
        _logTextBox.AppendText(text + Environment.NewLine);
    }

    public void EnableOkButton()
    {
        if (!IsDisposed)
            _okButton.Enabled = true;
    }
}