using System.Reflection;
using RunFence.Launch;

namespace RunFence.Licensing.UI.Forms;

public partial class AboutPanel : UserControl
{
    private readonly ILicenseService _licenseService;
    private readonly ILaunchFacade _launchFacade;

    public AboutPanel(ILicenseService licenseService, ILaunchFacade launchFacade)
    {
        _licenseService = licenseService;
        _launchFacade = launchFacade;
        InitializeComponent();
        PopulateContent();

        // Refresh when license status changes externally (e.g., activation from another dialog,
        // or expiry detected by ShouldShowNag). LicenseStatusChanged may fire on any thread,
        // so marshal to the UI thread before touching controls.
        licenseService.LicenseStatusChanged += OnLicenseStatusChanged;
    }

    partial void OnDisposing()
    {
        _licenseService.LicenseStatusChanged -= OnLicenseStatusChanged;
    }

    private void OnLicenseStatusChanged()
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        BeginInvoke(PopulateContent);
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
        using var dlg = new EvaluationNagDialog(_licenseService, _launchFacade, skipCountdown: true);
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

    private void OpenUrl(string url) =>
        _launchFacade.LaunchUrl(url, AccountLaunchIdentity.InteractiveUser);
}