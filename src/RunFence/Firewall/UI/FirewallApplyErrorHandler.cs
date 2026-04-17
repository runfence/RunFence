namespace RunFence.Firewall.UI;

/// <summary>
/// Displays a standardized error message when firewall rule apply fails with rollback.
/// A static class wrapping a single MessageBox.Show call is acceptable here: the method has
/// no dependencies, no I/O beyond the message box itself, and is not a candidate for mocking
/// (callers handle rollback logic independently). Extracting it into a DI-registered service
/// would add complexity without benefit.
/// </summary>
public static class FirewallApplyErrorHandler
{
    public static void ShowApplyFailure(
        IWin32Window? owner, FirewallApplyException applyException, Exception? rollbackError)
    {
        var message = $"Failed to apply firewall rules. Previous firewall settings were restored: {applyException.CauseMessage}";
        if (rollbackError != null)
            message += $"\n\nSaving the restored settings also failed: {rollbackError.Message}";
        MessageBox.Show(owner, message, "RunFence", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
