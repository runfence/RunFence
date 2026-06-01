using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public interface IShortcutProtectionStateStore
{
    ShortcutProtectionState? Load(string appId, string shortcutPath);
    void Save(string appId, ShortcutProtectionState state);
    void Delete(string appId, string shortcutPath);
    void PruneMissingFiles(string appId);
}
