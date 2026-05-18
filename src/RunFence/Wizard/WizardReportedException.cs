namespace RunFence.Wizard;

/// <summary>
/// Signals that a wizard step or helper already reported a user-facing error and the wizard
/// should stop the current operation without wrapping the failure in another UI message.
/// </summary>
public sealed class WizardReportedException : Exception
{
    public WizardReportedException(string message)
        : base(message)
    {
    }

    public WizardReportedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
