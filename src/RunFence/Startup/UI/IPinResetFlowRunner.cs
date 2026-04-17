using RunFence.Core.Models;

namespace RunFence.Startup.UI;

public interface IPinResetFlowRunner
{
    /// <summary>
    /// Runs the full PIN reset flow inside the current secure desktop context.
    /// Must be called from within an <see cref="ISecureDesktopRunner.Run"/> callback (or on the UI thread).
    /// </summary>
    /// <param name="extraStoreInit">
    /// Optional action invoked on the new <see cref="CredentialStore"/> before saving.
    /// Used by the lock manager flow to call <see cref="IAppInitializationHelper.EnsureCurrentAccountCredential"/>.
    /// </param>
    /// <returns>
    /// The new <see cref="CredentialStore"/> and derived key bytes if the reset completed successfully;
    /// <see langword="null"/> if the user cancelled or skipped.
    /// </returns>
    (CredentialStore Store, byte[] Key)? RunResetFlow(Action<CredentialStore>? extraStoreInit = null);
}
