using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Security;

namespace RunFence.Startup.UI;

/// <summary>
/// Abstracts UI interactions needed during the startup sequence: PIN prompts, error dialogs,
/// and takeover confirmation. Implemented by <see cref="StartupUI"/> in the WinForms layer.
/// </summary>
public interface IStartupUI
{
    /// <summary>
    /// Prompts the user to set a new PIN (first run). Returns the owned result object,
    /// or null on cancel.
    /// </summary>
    PinResetResult? PromptNewPin();

    /// <summary>
    /// Prompts the user to verify their PIN. Uses injected services internally for
    /// verification and key derivation. When the user resets their PIN,
    /// <see cref="PinVerifyOutcome.NewStore"/> is set.
    /// </summary>
    PinVerifyOutcome PromptVerifyPin(
        CredentialStore store,
        byte[]? configSalt);

    /// <summary>
    /// Shows a DPAPI-loss warning then prompts the user to enter a recovery PIN.
    /// Returns the owned recovery result, or null on cancel.
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
    /// Shows a configuration decryption error and asks whether to restore a loaded-good backup,
    /// start fresh with an empty config, or exit.
    /// </summary>
    StartupConfigRecoveryChoice ConfirmStartFresh(bool backupAvailable);

    /// <summary>
    /// Shows a credential store load error and asks whether to restore a loaded-good backup.
    /// </summary>
    bool ConfirmRestoreCredentialStoreBackup();

    MainConfigPinPromptResult PromptMainConfigMismatchPin(
        string configPath,
        Func<ProtectedString, MainConfigPinVerificationResult> verifyPin);
}

public enum StartupConfigRecoveryChoice
{
    Exit,
    StartFresh,
    UseBackup
}
