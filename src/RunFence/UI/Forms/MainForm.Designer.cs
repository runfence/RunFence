#nullable disable

using System.ComponentModel;

namespace RunFence.UI.Forms;

partial class MainForm
{
    private IContainer components = null;

    private TabControl _tabControl;
    private TabPage _applicationsTab;
    private TabPage _accountsTab;
    private TabPage _groupsTab;
    private TabPage _optionsTab;
    private TabPage _aboutTab;

    private MainForm() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _configHandler?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _tabControl = new TabControl();
        _applicationsTab = new TabPage();
        _accountsTab = new TabPage();
        _groupsTab = new TabPage();
        _optionsTab = new TabPage();
        _aboutTab = new TabPage();

        SuspendLayout();

        // _tabControl
        _tabControl.Dock = DockStyle.Fill;
        _tabControl.Padding = new Point(12, 6);

        // _applicationsTab
        _applicationsTab.Text = "Applications";

        // _accountsTab
        _accountsTab.Text = "Accounts";

        // _groupsTab
        _groupsTab.Text = "Groups";

        // _optionsTab
        _optionsTab.Text = "Options";

        // _aboutTab
        _aboutTab.Text = "About";

        _tabControl.TabPages.Add(_applicationsTab);
        _tabControl.TabPages.Add(_accountsTab);
        _tabControl.TabPages.Add(_groupsTab);
        _tabControl.TabPages.Add(_optionsTab);
        _tabControl.TabPages.Add(_aboutTab);

        // MainForm
        Text = "RunFence";
        Size = new Size(1181, 675);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_tabControl);

        ResumeLayout(false);
        PerformLayout();
    }
}
