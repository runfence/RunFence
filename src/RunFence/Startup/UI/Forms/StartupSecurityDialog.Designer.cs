#nullable disable

using System.ComponentModel;

namespace RunFence.Startup.UI.Forms;

partial class StartupSecurityDialog
{
    private IContainer components = null;

    private Label _warningLabel;
    private Label _descLabel;
    private ListView _listView;
    private Label _remediationLabel;
    private Button _openLocationButton;
    private Button _copyButton;
    private Button _dismissButton;

    private StartupSecurityDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _warningLabel = new Label();
        _descLabel = new Label();
        _listView = new ListView();
        _remediationLabel = new Label();
        _openLocationButton = new Button();
        _copyButton = new Button();
        _dismissButton = new Button();

        SuspendLayout();

        // _warningLabel
        _warningLabel.Text = "Writable startup locations detected";
        _warningLabel.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        _warningLabel.ForeColor = Color.FromArgb(0xCC, 0x77, 0x00);
        _warningLabel.Location = new Point(15, 12);
        _warningLabel.AutoSize = true;

        // _descLabel
        _descLabel.Text = "The following startup locations are writable by non-administrator accounts. " +
                          "This is a potential privilege escalation vector \u2014 a standard account could " +
                          "place a program that runs automatically when an administrator logs in.";
        _descLabel.Location = new Point(15, 40);
        _descLabel.Size = new Size(970, 45);
        _descLabel.AutoSize = false;

        // _listView
        _listView.Location = new Point(15, 92);
        _listView.Size = new Size(970, 550);
        _listView.View = View.Details;
        _listView.FullRowSelect = true;
        _listView.GridLines = true;
        _listView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _listView.ShowGroups = true;
        _listView.Columns.Add("Location", 500);
        _listView.Columns.Add("Principal", 240);
        _listView.Columns.Add("Access", 200);
        _listView.Groups.Add(new ListViewGroup("Startup Folders", HorizontalAlignment.Left) { Name = "StartupFolder" });
        _listView.Groups.Add(new ListViewGroup("Registry Autorun Keys", HorizontalAlignment.Left) { Name = "RegistryRunKey" });
        _listView.Groups.Add(new ListViewGroup("Autorun Executables", HorizontalAlignment.Left) { Name = "AutorunExecutable" });
        _listView.Groups.Add(new ListViewGroup("Task Scheduler", HorizontalAlignment.Left) { Name = "TaskScheduler" });
        _listView.Groups.Add(new ListViewGroup("Logon Scripts", HorizontalAlignment.Left) { Name = "LogonScript" });
        _listView.Groups.Add(new ListViewGroup("Auto-Start Services", HorizontalAlignment.Left) { Name = "AutoStartService" });
        _listView.Groups.Add(new ListViewGroup("Disk Root ACLs", HorizontalAlignment.Left) { Name = "DiskRootAcl" });
        _listView.Groups.Add(new ListViewGroup("Account Policy", HorizontalAlignment.Left) { Name = "AccountPolicy" });
        _listView.Groups.Add(new ListViewGroup("Windows Firewall", HorizontalAlignment.Left) { Name = "FirewallPolicy" });

        // _remediationLabel
        _remediationLabel.Text = "Recommended fix: Remove this principal from the drive root ACL. " +
                                 "Then use allow-mode permissions on specific subfolders to grant only the access they actually need.";
        _remediationLabel.Location = new Point(15, 648);
        _remediationLabel.Size = new Size(970, 40);
        _remediationLabel.AutoSize = false;
        _remediationLabel.ForeColor = SystemColors.GrayText;
        _remediationLabel.Visible = false;

        // _openLocationButton
        _openLocationButton.Text = "Open Location";
        _openLocationButton.Location = new Point(610, 700);
        _openLocationButton.Size = new Size(130, 28);
        _openLocationButton.FlatStyle = FlatStyle.System;
        _openLocationButton.Enabled = false;
        _openLocationButton.Click += OnOpenLocationClick;

        // _copyButton
        _copyButton.Text = "Copy to Clipboard";
        _copyButton.Location = new Point(755, 700);
        _copyButton.Size = new Size(140, 28);
        _copyButton.FlatStyle = FlatStyle.System;
        _copyButton.Click += OnCopyClick;

        // _dismissButton
        _dismissButton.Text = "Dismiss";
        _dismissButton.DialogResult = DialogResult.OK;
        _dismissButton.Location = new Point(905, 700);
        _dismissButton.Size = new Size(80, 28);
        _dismissButton.FlatStyle = FlatStyle.System;

        // StartupSecurityDialog
        Text = "Startup Security Warning";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 742);
        AcceptButton = _dismissButton;
        CancelButton = _dismissButton;
        Controls.AddRange(new Control[] { _warningLabel, _descLabel, _listView, _remediationLabel, _openLocationButton, _copyButton, _dismissButton });

        ResumeLayout(false);
        PerformLayout();
    }
}
