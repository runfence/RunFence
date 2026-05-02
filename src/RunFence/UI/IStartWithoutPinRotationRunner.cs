using RunFence.Core.Models;

namespace RunFence.UI;

/// <summary>
/// Holds the result of a successful secure-desktop PIN verification and key rotation.
/// </summary>
public record struct PinRotationResult(CredentialStore NewStore, byte[] NewKey);

/// <summary>
/// Executes the secure-desktop PIN verify + change-PIN flow needed to rotate the PIN-derived key
/// when enabling or disabling the Start Without PIN feature.
/// The production implementation owns <see cref="RunFence.Infrastructure.IModalCoordinator"/>,
/// the <see cref="RunFence.Startup.UI.Forms.PinDialog"/> construction, PIN verification via
/// <see cref="RunFence.Security.IPinService.VerifyPin"/>, and re-encryption via
/// <see cref="RunFence.Security.IPinService.ChangePin"/>.
/// Tests supply a fake runner so no WinForms dialogs are opened.
/// </summary>
public interface IStartWithoutPinRotationRunner
{
    /// <summary>
    /// Runs the secure-desktop verify+rotate flow with the given <paramref name="promptMessage"/>.
    /// Returns <c>null</c> when the user cancelled or an error occurred.
    /// Returns a <see cref="PinRotationResult"/> on success; the caller is responsible for
    /// zeroing <see cref="PinRotationResult.NewKey"/> after consuming it.
    /// </summary>
    PinRotationResult? Run(string promptMessage, SessionContext session);
}
