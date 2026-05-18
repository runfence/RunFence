using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.RunAs;

public sealed class RunAsCreatedAccountPostSetupService(
    IAppStateProvider appState,
    RunAsAccountSettingsApplier settingsApplier,
    IRunAsPermissionPromptHelper permissionPromptHelper)
{
    public async Task<RunAsCreatedAccountPostSetupResult> CompleteAsync(
        RunAsCreatedAccountPostSetupRequest request,
        CredentialEntry credential)
    {
        if (string.IsNullOrEmpty(request.CreatedSid))
            throw new InvalidOperationException("Missing SID for created RunAs account.");

        if (string.IsNullOrEmpty(request.Username))
            throw new InvalidOperationException("Missing username for created RunAs account.");

        var warningMessages = request.Errors.ToList();

        if (request.IsEphemeral)
            appState.Database.GetOrCreateAccount(request.CreatedSid).DeleteAfterUtc = DateTime.UtcNow.AddHours(24);

        settingsApplier.ApplyLaunchDefaults(request.CreatedSid, request.SelectedPrivilegeLevel);

        if (request.FirewallSettingsChanged)
        {
            settingsApplier.ApplyFirewallDbSettings(
                request.CreatedSid,
                request.AllowInternet,
                request.AllowLocalhost,
                request.AllowLan);
        }

        await settingsApplier.RunPostCreationTasksAsync(
            request.CreatedSid,
            request.Username,
            request.SettingsImportPath,
            request.FirewallSettingsChanged,
            warningMessages);

        RunAsCreatedAccountPostSetupResult result;
        try
        {
            var permissionGrant = permissionPromptHelper.PromptIfNeeded(request.FilePath, credential.Sid);
            result = new RunAsCreatedAccountPostSetupResult(
                permissionGrant,
                WasCanceled: false,
                warningMessages.ToArray());
        }
        catch (OperationCanceledException)
        {
            result = new RunAsCreatedAccountPostSetupResult(
                null,
                WasCanceled: true,
                warningMessages.ToArray());
        }

        return result;
    }
}
