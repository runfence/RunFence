namespace RunFence.Infrastructure;

public readonly record struct ProcessIdentitySnapshot(
    string? ImagePath,
    string? OwnerSid,
    int? IntegrityLevel);
