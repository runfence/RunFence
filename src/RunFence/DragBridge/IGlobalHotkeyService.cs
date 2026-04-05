namespace RunFence.DragBridge;

public interface IGlobalHotkeyService : IDisposable
{
    event Action<int>? HotkeyPressed;
    bool Register(int id, int modifiers, int key, bool consume = true);
    void Unregister(int id);
    void UnregisterAll();
}