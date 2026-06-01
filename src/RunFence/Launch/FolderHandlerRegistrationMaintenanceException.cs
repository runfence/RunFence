namespace RunFence.Launch;

public sealed class FolderHandlerRegistrationMaintenanceException(
    string message,
    Exception innerException,
    FolderHandlerRegistrationMaintenanceResult maintenanceResult)
    : InvalidOperationException(message, innerException)
{
    public FolderHandlerRegistrationMaintenanceResult MaintenanceResult { get; } = maintenanceResult;
}
