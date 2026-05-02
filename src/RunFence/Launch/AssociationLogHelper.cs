namespace RunFence.Launch;

public static class AssociationLogHelper
{
    public static string FormatProgId(string? progId)
        => string.IsNullOrWhiteSpace(progId) ? string.Empty : $" (ProgId '{progId}')";
}
