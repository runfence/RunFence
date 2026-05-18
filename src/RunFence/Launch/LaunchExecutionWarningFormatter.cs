namespace RunFence.Launch;

public static class LaunchExecutionWarningFormatter
{
    public static string? Format(string startedItem, LaunchExecutionResult? result)
        => Format(startedItem, result?.MaintenanceWarnings);

    public static string? Format(string startedItem, IReadOnlyList<string>? maintenanceWarnings)
    {
        if (maintenanceWarnings == null || maintenanceWarnings.Count == 0)
            return null;

        return $"{startedItem} started, but RunFence could not finish some post-launch maintenance:\n\n"
               + string.Join("\n\n", maintenanceWarnings);
    }
}
