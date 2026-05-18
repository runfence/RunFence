using RunFence.Core.Helpers;
using RunFence.Core;

namespace RunFence.Launcher;

public sealed class LauncherAssociationFallbackCommandResolver(ILauncherAssociationFallbackLookup lookup)
    : ILauncherAssociationFallbackCommandResolver
{
    public string? ResolveStoredFallbackCommandForAssociation(string association)
    {
        var fallbackValue = lookup.ReadFallbackValue(association);
        return ResolveStoredFallbackCommand(fallbackValue);
    }

    public string? ResolveStoredFallbackCommand(string? fallbackValue)
    {
        if (string.IsNullOrEmpty(fallbackValue))
            return null;

        if (AssociationCommandHelper.IsRunFenceProgId(fallbackValue))
            return null;

        if (IsRunFenceFallbackCommand(fallbackValue))
            return null;

        var progResult = lookup.ResolveMergedProgIdCommand(fallbackValue);
        return progResult.Status switch
        {
            LauncherFallbackCommandLookupStatus.Resolved => IsRunFenceFallbackCommand(progResult.Command) ? null : progResult.Command,
            LauncherFallbackCommandLookupStatus.RejectedRunFenceCommand => null,
            LauncherFallbackCommandLookupStatus.NotFound => fallbackValue,
            _ => null
        };
    }

    public string? ResolveHklmFallbackCommand(string association)
    {
        var command = lookup.ResolveHklmAssociationCommand(association);
        return IsRunFenceFallbackCommand(command) ? null : command;
    }

    private static bool IsRunFenceFallbackCommand(string? fallbackValue)
    {
        if (string.IsNullOrWhiteSpace(fallbackValue))
            return true;

        if (AssociationCommandHelper.IsRunFenceAssociationLauncherCommand(fallbackValue)
            || AssociationCommandHelper.IsRunFenceLauncherCommand(fallbackValue))
        {
            return true;
        }

        var exePath = AssociationRegistryCommandParser.ExtractExeFromCommand(fallbackValue);
        return AssociationCommandHelper.IsRunFenceLauncherExecutablePath(exePath?.Trim().Trim('"'));
    }
}
