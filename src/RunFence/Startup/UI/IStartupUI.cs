using RunFence.Core.Models;

namespace RunFence.Startup.UI;

/// <summary>
/// Abstracts UI interactions needed during the startup sequence: PIN prompts, error dialogs,
/// and takeover confirmation. Implemented by <see cref="StartupUI"/> in the WinForms layer.
/// </summary>
public interface IStartupUI
{
    /// <summary>
    /// Prompts the user to set a new PIN (first run). Returns the new PIN key and
    /// created credential store, or null on cancel.
    /// </summary>
    (CredentialStore store, byte[] key)? PromptNewPin();

    /// <summary>
    /// Prompts the user to verify their PIN. Uses injected services internally for
    /// verification and key derivation. Returns an outcome with Key.Length == 0 on cancel.
    /// When the user resets their PIN, <see cref="PinVerifyOutcome.NewStore"/> is set.
    /// </summary>
    PinVerifyOutcome PromptVerifyPin(
        CredentialStore store,
        byte[]? configSalt);

    /// <summary>
    /// Shows a DPAPI-loss warning then prompts the user to enter a recovery PIN.
    /// Returns the new credential store and key, or null on cancel.
    /// </summary>
    RecoveryPinOutcome? PromptRecoveryPin(byte[]? configSalt);

    /// <summary>Shows an error message box to the user.</summary>
    void ShowError(string message, string title = "Error");

    /// <summary>
    /// Asks the user whether to take over the session from another instance.
    /// Returns true if the user confirmed, false to abort.
    /// </summary>
    bool ConfirmTakeover(bool isFirstRun, bool isBackground);

    /// <summary>
    /// Shows a configuration decryption error and asks whether to start fresh with an empty config.
    /// Returns true if the user chose to start fresh, false to exit.
    /// </summary>
    bool ConfirmStartFresh();
}

/// <summary>Outcome of a PIN verification prompt.</summary>
public record struct PinVerifyOutcome(byte[] Key, CredentialStore? NewStore, byte[]? MismatchKey);

/// <summary>Outcome of a recovery PIN prompt.</summary>
public record struct RecoveryPinOutcome(CredentialStore Store, byte[] Key, byte[]? MismatchKey);