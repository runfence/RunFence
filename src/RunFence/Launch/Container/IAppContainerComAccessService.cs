namespace RunFence.Launch.Container;

/// <summary>
/// Manages COM activation and access permissions for AppContainer SIDs.
/// </summary>
public interface IAppContainerComAccessService
{
    AppContainerComAccessResult GrantComAccess(string containerSid, string clsid);
    AppContainerComAccessResult RevokeComAccess(string containerSid, string clsid);
}
