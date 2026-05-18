using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.DragBridge;

public class GlobalHotkeyService : IGlobalHotkeyService, IRequiresInitialization
{
    private readonly ILoggingService _log;
    private readonly IHotkeySource _hotkeySource;
    private readonly HashSet<int> _registeredIds = [];

    public event Action<int>? HotkeyPressed;

    public GlobalHotkeyService(ILoggingService log, IHotkeySource hotkeySource)
    {
        _log = log;
        _hotkeySource = hotkeySource;
    }

    public void Initialize()
    {
        _hotkeySource.HotkeyPressed += OnHotkey;
    }

    public bool Register(int id, int modifiers, int key, bool consume = true)
    {
        var result = _hotkeySource.RegisterHotkey(id, modifiers, key, consume);
        if (result.Status is HotkeyRegistrationStatus.Succeeded or HotkeyRegistrationStatus.AlreadyRegistered)
        {
            _registeredIds.Add(id);
            return true;
        }

        if (!string.IsNullOrEmpty(result.Error))
            _log.Warn($"GlobalHotkeyService: hotkey register failed for id={id}: {result.Error}");
        return false;
    }

    public void Unregister(int id)
    {
        _registeredIds.Remove(id);
        _hotkeySource.UnregisterHotkey(id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _registeredIds.ToList())
            _hotkeySource.UnregisterHotkey(id);
        _registeredIds.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _hotkeySource.HotkeyPressed -= OnHotkey;
        _hotkeySource.Dispose();
    }

    private void OnHotkey(int id)
    {
        if (_registeredIds.Contains(id))
            HotkeyPressed?.Invoke(id);
    }
}
