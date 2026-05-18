namespace RunFence.Licensing;

public class LicenseMessageFormatter : ILicenseMessageFormatter
{
    public string FormatRestrictionMessage(FeatureRestrictionResult restriction)
        => restriction.MessageKey switch
        {
            "Apps" => $"Evaluation mode allows up to {restriction.ConfiguredLimit} app entries. Activate a license to remove this limit.",
            "Containers" => $"Evaluation mode allows up to {restriction.ConfiguredLimit} AppContainer profiles. Activate a license to remove this limit.",
            "HiddenAccounts" => $"Evaluation mode allows up to {restriction.ConfiguredLimit} hidden accounts. Activate a license to remove this limit.",
            "Credentials" => $"Evaluation mode allows up to {restriction.ConfiguredLimit} stored credentials. Activate a license to remove this limit.",
            "FirewallAllowlist" => $"Evaluation mode allows up to {restriction.ConfiguredLimit} firewall allowlist entries. Activate a license to remove this limit.",
            _ => $"Evaluation mode allows up to {restriction.ConfiguredLimit} entries. Activate a license to remove this limit."
        };
}
