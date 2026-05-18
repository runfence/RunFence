namespace RunFence.Licensing;

public record MachineIdentityResult(
    MachineIdentityStatus Status,
    MachineIdentitySource? Source,
    string? CanonicalSourceValue,
    byte[]? MachineIdHash,
    string? MachineCode,
    string? ErrorText);
