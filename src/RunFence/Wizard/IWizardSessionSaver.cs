namespace RunFence.Wizard;

/// <summary>
/// Persists wizard session changes to disk and triggers UI refresh.
/// Implemented in <c>ServiceContainerBuilder</c> as an adapter over the panel save infrastructure.
/// Using an interface avoids passing a delegate to the constructor (CLAUDE.md guideline).
/// </summary>
public interface IWizardSessionSaver
{
    /// <summary>
    /// Saves the current session state to disk and refreshes all panels.
    /// </summary>
    void SaveAndRefresh();
}