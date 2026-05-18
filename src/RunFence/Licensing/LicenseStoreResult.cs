namespace RunFence.Licensing;

public record LicenseStoreResult(
    LicenseStoreStatus Status,
    string? LicenseKey,
    string? ErrorText);
