using RunFence.Core.Models;
using RunFence.Licensing;

namespace RunFence.Firewall.UI;

/// <summary>
/// Validates and builds firewall allowlist entries. Handles IP/CIDR and domain validation,
/// duplicate detection, and license limit checking.
/// </summary>
public class FirewallAllowlistValidator(ILicenseService licenseService)
{
    /// <summary>
    /// Validates a raw input value and constructs a <see cref="FirewallAllowlistEntry"/> if it is a
    /// valid IP address, CIDR range, or domain name. Returns null if the value is invalid.
    /// Does not check for duplicates — call <see cref="HasDuplicate"/> separately.
    /// </summary>
    public FirewallAllowlistEntry? ValidateEntry(string value)
    {
        if (FirewallAddressRangeBuilder.IsValidIpOrCidr(value))
            return new FirewallAllowlistEntry { Value = value, IsDomain = false };

        if (IsValidDomain(value))
            return new FirewallAllowlistEntry { Value = value, IsDomain = true };

        return null;
    }

    /// <summary>
    /// Returns true if an entry with the same value already exists in the list (case-insensitive).
    /// </summary>
    public bool HasDuplicate(string value, IEnumerable<FirewallAllowlistEntry> currentEntries)
        => currentEntries.Any(en => string.Equals(en.Value, value, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns true if the value is a syntactically valid domain name.
    /// Single-label names (e.g. "intranet", "myserver") are accepted by design —
    /// corporate intranets commonly use unqualified hostnames for internal resources.
    /// </summary>
    public bool IsValidDomain(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (value.Length > 253)
            return false;
        foreach (var label in value.Split('.'))
        {
            if (label.Length is 0 or > 63)
                return false;
            foreach (var ch in label)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '-')
                    return false;
            }

            if (label[0] == '-' || label[^1] == '-')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true if an additional entry can be added under the current license.
    /// </summary>
    public bool CheckLicenseLimit(int currentCount)
        => licenseService.CanAddFirewallAllowlistEntry(currentCount);

    /// <summary>
    /// Returns the license restriction message for the firewall allowlist feature.
    /// </summary>
    public string? GetLicenseLimitMessage(int currentCount)
        => licenseService.GetRestrictionMessage(EvaluationFeature.FirewallAllowlist, currentCount);
}
