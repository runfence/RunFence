using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Security;
using RunFence.Startup.UI;

namespace RunFence.UI;

/// <summary>
/// Handles the "Start without PIN" feature toggle for <see cref="Forms.OptionsPanel"/>.
/// </summary>
public class OptionsStartWithoutPinHandler(
    IRememberPinService rememberPinService,
    PinChangeOrchestrator pinChangeOrchestrator,
    ISessionProvider sessionProvider,
    IStartWithoutPinPromptService promptService,
    IStartWithoutPinRotationRunner rotationRunner,
    ILicenseService licenseService,
    ILoggingService log)
{
    public bool IsLicensed => licenseService.IsLicensed;
    public bool IsStartWithoutPinEnabled => rememberPinService.IsEnabled;

    /// <summary>
    /// Enables or disables the "Start without PIN" feature.
    /// <paramref name="onKeyRotated"/> is called after successful key rotation.
    /// Failures (TPM unavailable, PIN rejected, user cancelled) are handled internally;
    /// callers should re-read <see cref="IsStartWithoutPinEnabled"/> to sync UI state.
    /// </summary>
    public void SetStartWithoutPin(
        bool enable,
        Action onKeyRotated)
    {
        if (enable)
            EnableStartWithoutPin(onKeyRotated);
        else
            DisableStartWithoutPin(onKeyRotated);
    }

    private void EnableStartWithoutPin(Action onKeyRotated)
    {
        if (!promptService.ConfirmSecurityWarning())
            return;

        var tpmAvailable = rememberPinService.IsTpmAvailable();
        if (!tpmAvailable)
        {
            if (!promptService.ConfirmDpapiOnlyWarning())
                return;
        }

        var session = sessionProvider.GetSession();
        var rotationResult = rotationRunner.Run("Confirm PIN to enable Start Without PIN:", session);
        if (rotationResult == null)
            return;
        using (rotationResult)
        {
            RunRotationAndApply(session, rotationResult, onKeyRotated);
        }

        try
        {
            if (tpmAvailable)
            {
                try
                {
                    rememberPinService.EnableWithTpm(session.PinDerivedKey);
                }
                catch (Exception tpmEx)
                {
                    log.Warn($"TPM encryption failed, falling back to DPAPI-only: {tpmEx.Message}");
                    rememberPinService.EnableDpapiOnly(session.PinDerivedKey);
                    promptService.ShowTpmFallbackWarning();
                }
            }
            else
            {
                rememberPinService.EnableDpapiOnly(session.PinDerivedKey);
            }
        }
        catch (Exception ex)
        {
            TryDisableRememberPinAfterFailure();
            log.Error("Failed to enable remembered PIN key after key rotation", ex);
            promptService.ShowError($"Failed to enable PIN bypass: {ex.Message}", "Error");
        }
    }

    private void DisableStartWithoutPin(Action onKeyRotated)
    {
        var session = sessionProvider.GetSession();
        var rotationResult = rotationRunner.Run("Confirm PIN to disable Start Without PIN:", session);
        if (rotationResult == null)
            return;

        try
        {
            rememberPinService.Disable();
        }
        catch (Exception ex)
        {
            rotationResult.Dispose();
            log.Error("Failed to disable remembered PIN key", ex);
            promptService.ShowError($"Failed to disable PIN bypass: {ex.Message}", "Error");
            return;
        }

        using (rotationResult)
        {
            RunRotationAndApply(session, rotationResult, onKeyRotated);
        }
    }

    private void RunRotationAndApply(
        SessionContext session,
        PinKeyRotationResult result,
        Action onKeyRotated)
        => pinChangeOrchestrator.ApplyKeyRotation(session, result, onKeyRotated, updateRememberPin: false);

    private void TryDisableRememberPinAfterFailure()
    {
        try
        {
            rememberPinService.Disable();
        }
        catch (Exception cleanupEx)
        {
            log.Warn($"Failed to clean up remembered PIN key material after enable failure: {cleanupEx.Message}");
        }
    }
}
