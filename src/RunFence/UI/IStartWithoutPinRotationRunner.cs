using RunFence.Core.Models;
using RunFence.Security;

namespace RunFence.UI;

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
    /// </summary>
    PinKeyRotationResult? Run(string promptMessage, SessionContext session);
}
