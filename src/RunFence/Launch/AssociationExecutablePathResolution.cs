namespace RunFence.Launch;

public sealed record AssociationExecutablePathResolution(
    bool IsValid,
    string ExePath,
    string RejectionReason,
    bool WasRepaired)
{
    public static AssociationExecutablePathResolution Valid(string exePath, bool wasRepaired = false) =>
        new(true, exePath, string.Empty, wasRepaired);

    public static AssociationExecutablePathResolution Invalid(string exePath, string rejectionReason) =>
        new(false, exePath, rejectionReason, false);
}
