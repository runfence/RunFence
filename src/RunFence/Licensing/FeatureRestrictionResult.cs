namespace RunFence.Licensing;

public record FeatureRestrictionResult(
    bool Allowed,
    string RestrictionCode,
    int CurrentCount,
    int AttemptedCount,
    int ConfiguredLimit,
    string MessageKey);
