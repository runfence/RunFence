namespace RunFence.Persistence.UI;

public sealed record LoadAppsResult(
    bool Succeeded,
    string? ErrorMessage,
    IReadOnlyList<string>? Warnings = null,
    bool BackupAvailable = false)
{
    public void Deconstruct(out bool success, out string? errorMessage)
    {
        success = Succeeded;
        errorMessage = ErrorMessage;
    }

}
