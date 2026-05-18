using System.Diagnostics;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Ipc;

namespace RunFence.Launcher;

public sealed class LauncherAssociationFallbackService(
    AssociationFallbackRestoreService restoreService,
    ILauncherAssociationFallbackCommandResolver fallbackResolver,
    ILauncherProcessStarter processStarter,
    ILauncherUserNotifier notifier)
    : ILauncherAssociationFallbackService
{
    public int LaunchFallback(string association, string? rawArguments)
    {
        var fallbackCommand = GetFallbackCommand(association);
        return LaunchResolvedCommand(association, fallbackCommand, rawArguments);
    }

    public int CleanupAndLaunchFallback(string association, string? rawArguments)
    {
        var restoreResult = restoreService.RestoreFromFallback(
            association,
            FallbackCleanupMode.RemoveRunFenceOverrideThenRestoreFallback);

        var fallbackCommand = fallbackResolver.ResolveStoredFallbackCommand(restoreResult.FallbackValue);
        return LaunchResolvedCommand(association, fallbackCommand ?? ResolveHklmFallbackCommand(association), rawArguments);
    }

    private string? GetFallbackCommand(string association)
    {
        return fallbackResolver.ResolveStoredFallbackCommandForAssociation(association)
            ?? ResolveHklmFallbackCommand(association);
    }

    private string? ResolveHklmFallbackCommand(string association)
        => fallbackResolver.ResolveHklmFallbackCommand(association);

    private int LaunchResolvedCommand(string association, string? command, string? rawArguments)
    {
        if (command == null)
        {
            notifier.ShowError("No fallback handler found for '" + association + "'.");
            return 1;
        }

        try
        {
            if (!AssociationCommandHelper.TryMaterializeCommand(
                    command,
                    rawArguments,
                    out var materialization,
                    out var rejectionReason))
            {
                notifier.ShowError("Failed to launch fallback handler: " + rejectionReason);
                return 1;
            }

            processStarter.Start(new ProcessStartInfo
            {
                FileName = materialization.ExePath,
                Arguments = materialization.Arguments ?? string.Empty,
                UseShellExecute = true
            });

            return 0;
        }
        catch (Exception ex)
        {
            notifier.ShowError("Failed to launch fallback handler: " + ex.Message);
            return 1;
        }
    }
}
