using RunFence.Core.Models;

namespace RunFence.Licensing;

public record LicenseValidationResult(
    LicenseValidationStatus Status,
    LicenseInfo ParsedLicenseInfo,
    MachineIdentityStatus MachineIdentityStatus,
    string? ErrorText);
