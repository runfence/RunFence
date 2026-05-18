namespace RunFence.Account.UI;

public sealed record PackageInstallLaunchResult(IInstallProcess Process, IReadOnlyList<string> MaintenanceWarnings);
