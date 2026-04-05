using RunFence.Apps.UI;
using RunFence.Core.Models;

namespace RunFence.Licensing.UI.Forms;

public partial class LicenseActivationDialog : Form
{
    private readonly ILicenseService _licenseService;

    internal LicenseActivationDialog(ILicenseService licenseService)
    {
        _licenseService = licenseService;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var key = _keyTextBox.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            MessageBox.Show("Please enter a license key.", "License Key",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var result = _licenseService.ActivateLicense(key);
        if (result == LicenseActivationResult.Success)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        var message = result switch
        {
            LicenseActivationResult.InvalidSignature => "The license key signature is invalid. Please check the key and try again.",
            LicenseActivationResult.WrongVersion => "This license key was issued for a different version of RunFence.",
            LicenseActivationResult.WrongMachine => "This license key is registered to a different machine. " +
                                                    "Keys are machine-specific. Contact support if you need to transfer your license.",
            LicenseActivationResult.Expired => "This license key has expired. Please purchase a renewal.",
            LicenseActivationResult.Malformed => "The license key format is invalid. Please copy the full key exactly as received.",
            _ => "License activation failed. Please try again."
        };

        MessageBox.Show(message, "Activation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }
}