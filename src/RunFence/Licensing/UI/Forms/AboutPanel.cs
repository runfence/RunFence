using System.Diagnostics;
using System.Reflection;

namespace RunFence.Licensing.UI.Forms;

public partial class AboutPanel : UserControl
{
    private readonly ILicenseService _licenseService;

    internal AboutPanel(ILicenseService licenseService)
    {
        _licenseService = licenseService;
        InitializeComponent();
        PopulateContent();
    }

    private void PopulateContent()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        _versionLabel.Text = version is null ? "" : $"Version {version}";

        var info = _licenseService.GetLicenseInfo();
        if (info.IsValid)
        {
            var licensed = $"Licensed to: {info.LicenseeName}";
            if (info.ExpiryDate.HasValue)
                licensed += $"  (expires {info.ExpiryDate.Value:yyyy-MM-dd})";
            _licenseStatusLabel.Text = licensed;
            _licenseStatusLabel.ForeColor = SystemColors.ControlText;
            _activateLicenseButton.Visible = false;
        }
        else
        {
            _licenseStatusLabel.Text = "Evaluation Mode";
            _licenseStatusLabel.ForeColor = Color.FromArgb(80, 85, 100);
            _activateLicenseButton.Visible = true;
        }

        _pubKeyHashTextBox.Text = LicenseValidator.GetPublicKeyFingerprint();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        CenterContent();
    }

    private void CenterContent()
    {
        const int groupPadX = 16;
        const int groupPadY = 12;

        _contentPanel.Location = new Point(groupPadX, groupPadY);
        _contentGroupPanel.Size = new Size(
            _contentPanel.Width + groupPadX * 2,
            _contentPanel.Height + groupPadY * 2);
        _contentGroupPanel.Location = new Point(1, 1);
        _contentBorderPanel.Size = new Size(_contentGroupPanel.Width + 2, _contentGroupPanel.Height + 2);
        _contentBorderPanel.Location = new Point(
            Math.Max(16, (Width - _contentBorderPanel.Width) / 2),
            _headerPanel.Bottom + 16);
    }

    private void OnActivateLicenseClick(object? sender, EventArgs e)
    {
        using var dlg = new EvaluationNagDialog(_licenseService, skipCountdown: true);
        dlg.ShowDialog(this);
        if (_licenseService.IsLicensed)
            PopulateContent();
    }

    private void OnEmailLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        OpenUrl("mailto:runfencedev@gmail.com");
    }

    private void OnGitHubLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        OpenUrl("https://github.com/RunFence/");
    }

    private void OnReadmeLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        OpenUrl("https://github.com/runfence/RunFence/blob/master/README.md");
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}