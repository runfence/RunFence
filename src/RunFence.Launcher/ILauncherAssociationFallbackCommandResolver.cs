namespace RunFence.Launcher;

public interface ILauncherAssociationFallbackCommandResolver
{
    string? ResolveStoredFallbackCommandForAssociation(string association);
    string? ResolveStoredFallbackCommand(string? fallbackValue);
    string? ResolveHklmFallbackCommand(string association);
}
