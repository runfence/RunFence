namespace RunFence.Apps.UI;

public class AppEditDialogInputValidator
{
    public void EnsureConsistent(AppEditDialogInputSnapshot snapshot)
    {
        if (!string.IsNullOrEmpty(snapshot.SelectedAccountSid) &&
            !string.IsNullOrEmpty(snapshot.SelectedAppContainerName))
        {
            throw new InvalidOperationException(
                "App edit input cannot target both an account and an AppContainer.");
        }

        if (snapshot.OverrideIpcCallers && snapshot.IpcCallers == null)
            throw new InvalidOperationException(
                "App edit input must capture IPC callers when override is enabled.");
    }

    public string? Validate(AppEditDialogInputSnapshot snapshot)
    {
        if (snapshot.DuplicateEnvironmentVariableName != null)
            return $"Duplicate environment variable name: {snapshot.DuplicateEnvironmentVariableName}";

        return null;
    }
}
