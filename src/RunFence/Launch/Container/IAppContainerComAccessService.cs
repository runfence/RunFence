namespace RunFence.Launch.Container;

/// <summary>
/// Manages COM activation and access permissions for AppContainer SIDs.
/// </summary>
public interface IAppContainerComAccessService
{
    void GrantComAccess(string containerSid, string clsid);
    void RevokeComAccess(string containerSid, string clsid);
}
