namespace RunFence.Launcher;

public interface ILauncherAssociationFallbackLookup
{
    string? ReadFallbackValue(string association);
    LauncherFallbackCommandLookupResult ResolveMergedProgIdCommand(string progId);
    string? ResolveHklmAssociationCommand(string association);
}
