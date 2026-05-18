using RunFence.Core.Models;

namespace RunFence.Account.UI;

public record PostCreateSetupContext(
    string? SettingsImportPath,
    string CreatedSid,
    string NewUsername,
    bool FirewallSettingsChanged,
    List<InstallablePackage> SelectedInstallPackages,
    bool AllowInternet,
    List<string> Errors,
    List<string> Warnings);
