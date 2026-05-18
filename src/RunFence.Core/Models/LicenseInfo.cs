namespace RunFence.Core.Models;

public record LicenseInfo(
    bool IsValid,
    string? LicenseKey,
    string? LicenseeName,
    DateTime? ExpiryDate,
    LicenseTier? Tier,
    byte? MajorVersion,
    int? DaysRemaining)
{
    public static LicenseInfo Unlicensed { get; } = new(false, null, null, null, null, null, null);
}
