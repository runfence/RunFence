namespace RunFence.UI;

/// <summary>
/// Provides all user-visible prompts and messages for the Start Without PIN feature toggle.
/// Separates dialog interactions from orchestration logic in <see cref="OptionsStartWithoutPinHandler"/>.
/// </summary>
public interface IStartWithoutPinPromptService
{
    /// <summary>
    /// Shows the security warning explaining the storage mechanism and TPM usage.
    /// Returns true if the user confirmed (OK), false if cancelled.
    /// </summary>
    bool ConfirmSecurityWarning();

    /// <summary>
    /// Shows a warning that TPM is not available and the key will be stored with DPAPI-only protection.
    /// Returns true if the user confirmed (OK/Continue), false if cancelled.
    /// </summary>
    bool ConfirmDpapiOnlyWarning();

    /// <summary>
    /// Shows a non-blocking warning that TPM is present but does not support the required encryption
    /// operations; the key will be stored with DPAPI-only protection instead.
    /// </summary>
    void ShowTpmFallbackWarning();

    /// <summary>
    /// Shows an error message with the given title and text.
    /// </summary>
    void ShowError(string message, string title);
}
