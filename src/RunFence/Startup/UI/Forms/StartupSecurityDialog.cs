using RunFence.Apps.UI;
using RunFence.Core.Models;

namespace RunFence.Startup.UI.Forms;

public partial class StartupSecurityDialog : Form
{
    private readonly List<StartupSecurityFinding> _findings;
    private readonly FindingLocationHelper _findingLocationHelper;

    public StartupSecurityDialog(List<StartupSecurityFinding> findings, FindingLocationHelper findingLocationHelper)
    {
        _findings = findings;
        _findingLocationHelper = findingLocationHelper;
        InitializeComponent();
        _listView.Groups.Add(new ListViewGroup("Startup Folders", HorizontalAlignment.Left) { Name = "StartupFolder" });
        _listView.Groups.Add(new ListViewGroup("Registry Autorun Keys", HorizontalAlignment.Left) { Name = "RegistryRunKey" });
        _listView.Groups.Add(new ListViewGroup("Autorun Executables", HorizontalAlignment.Left) { Name = "AutorunExecutable" });
        _listView.Groups.Add(new ListViewGroup("Task Scheduler", HorizontalAlignment.Left) { Name = "TaskScheduler" });
        _listView.Groups.Add(new ListViewGroup("Logon Scripts", HorizontalAlignment.Left) { Name = "LogonScript" });
        _listView.Groups.Add(new ListViewGroup("Auto-Start Services", HorizontalAlignment.Left) { Name = "AutoStartService" });
        _listView.Groups.Add(new ListViewGroup("Disk Root ACLs", HorizontalAlignment.Left) { Name = "DiskRootAcl" });
        _listView.Groups.Add(new ListViewGroup("Account Policy", HorizontalAlignment.Left) { Name = "AccountPolicy" });
        _listView.Groups.Add(new ListViewGroup("Windows Firewall", HorizontalAlignment.Left) { Name = "FirewallPolicy" });
        _listView.Groups.Add(new ListViewGroup("Other", HorizontalAlignment.Left) { Name = "Other" });
        Icon = AppIcons.GetAppIcon();
        PopulateFindings();
        _listView.SelectedIndexChanged += (_, _) =>
        {
            var selected = _listView.SelectedItems.Count > 0
                ? _listView.SelectedItems[0].Tag as StartupSecurityFinding
                : null;
            _openLocationButton.Enabled = selected != null;
            _remediationLabel.Visible = selected?.Category == StartupSecurityCategory.DiskRootAcl;
        };
    }

    private void PopulateFindings()
    {
        foreach (var finding in _findings)
        {
            var group = finding.Category switch
            {
                StartupSecurityCategory.StartupFolder => _listView.Groups["StartupFolder"],
                StartupSecurityCategory.RegistryRunKey => _listView.Groups["RegistryRunKey"],
                StartupSecurityCategory.AutorunExecutable => _listView.Groups["AutorunExecutable"],
                StartupSecurityCategory.TaskScheduler => _listView.Groups["TaskScheduler"],
                StartupSecurityCategory.LogonScript => _listView.Groups["LogonScript"],
                StartupSecurityCategory.AutoStartService => _listView.Groups["AutoStartService"],
                StartupSecurityCategory.DiskRootAcl => _listView.Groups["DiskRootAcl"],
                StartupSecurityCategory.AccountPolicy => _listView.Groups["AccountPolicy"],
                StartupSecurityCategory.FirewallPolicy => _listView.Groups["FirewallPolicy"],
                _ => _listView.Groups["Other"]
            };
            var item = new ListViewItem(finding.TargetDescription)
            {
                Group = group,
                Tag = finding
            };
            item.SubItems.Add(finding.VulnerablePrincipal);
            item.SubItems.Add(finding.AccessDescription);
            _listView.Items.Add(item);
        }
    }

    private void OnOpenLocationClick(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count > 0 &&
            _listView.SelectedItems[0].Tag is StartupSecurityFinding finding)
        {
            _findingLocationHelper.OpenLocation(finding);
        }
    }

    private void OnCopyClick(object? sender, EventArgs e)
    {
        var lines = new List<string>
        {
            "Startup Security Findings",
            new string('-', 60)
        };
        foreach (var f in _findings)
        {
            lines.Add($"[{f.Category}] {f.TargetDescription}");
            lines.Add($"  Principal: {f.VulnerablePrincipal}");
            lines.Add($"  Access:    {f.AccessDescription}");
            lines.Add("");
        }

        Clipboard.SetText(string.Join(Environment.NewLine, lines));
        MessageBox.Show("Copied to clipboard.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}