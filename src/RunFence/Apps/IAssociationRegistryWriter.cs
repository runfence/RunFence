using RunFence.Core;

namespace RunFence.Apps;

public interface IAssociationRegistryWriter
{
    void AutoSetExtension(IRegistryKey hku, string sid, string key);
    void AutoSetProtocol(IRegistryKey hku, string sid, string key, string launcherPath);
    void AutoSetDirectClassExtension(IRegistryKey hku, string sid, string key, string className);
    void AutoSetDirectCommandExtension(IRegistryKey hku, string sid, string key, string command);
    void AutoSetDirectCommandProtocol(IRegistryKey hku, string sid, string key, string command);
    void RestoreKey(IRegistryKey hku, string sid, string key);
    void CleanStalePerUserProgIds(IRegistryKey classesKey, string contextDescription);
}
