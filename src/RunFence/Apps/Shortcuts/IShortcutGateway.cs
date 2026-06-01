namespace RunFence.Apps.Shortcuts;

public interface IShortcutGateway
{
    ShortcutData Read(string shortcutPath);
    ShortcutMutation ReadMutationState(string shortcutPath);
    void Write(string shortcutPath, ShortcutData data);
    void WriteMutationState(string shortcutPath, ShortcutMutation mutation);
    void Delete(string shortcutPath);
}
