namespace RunFence.Wizard;

/// <summary>
/// Allows wizard template executors to report status messages and non-fatal errors
/// during async execution, without depending on a specific UI implementation.
/// </summary>
public interface IWizardProgressReporter
{
    /// <summary>Reports a status message to show in the progress UI (e.g., "Applying firewall rules...").</summary>
    void ReportStatus(string message);

    /// <summary>Records a non-fatal error. Execution continues; errors are shown in the completion step.</summary>
    void ReportError(string message);
}