namespace RunFence.DragBridge;

public interface IHotkeySource : IDisposable
{
    event Action<int>? HotkeyPressed;
    HotkeyRegistrationResult RegisterHotkey(int id, int modifiers, int key, bool consume);
    HotkeyRegistrationResult UnregisterHotkey(int id);
}
