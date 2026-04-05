namespace RunFence.Core.Models;

public enum LicenseTier : byte
{
    Quarterly = 0x01,
    Annual = 0x02,
    Lifetime = 0x03
}

public enum LicenseActivationResult
{
    Success,
    InvalidSignature,
    WrongVersion,
    WrongMachine,
    Expired,
    Malformed
}

public enum EvaluationFeature
{
    Apps,
    Containers,
    HiddenAccounts,
    Credentials,
    FirewallAllowlist
}

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