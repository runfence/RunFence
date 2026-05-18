using Moq;
using RunFence.Core;
using RunFence.DragBridge;
using Xunit;

namespace RunFence.Tests;

public class GlobalHotkeyServiceTests
{
    [Fact]
    public void RegisterAndRaise_UsesHotkeySourceEvent()
    {
        var source = new FakeHotkeySource();
        var service = new GlobalHotkeyService(new Mock<ILoggingService>().Object, source);
        service.Initialize();
        service.Register(42, 0x0002, 0x4B);

        int? received = null;
        service.HotkeyPressed += id => received = id;
        source.Raise(42);

        Assert.Equal(42, received);
    }

    [Fact]
    public void Unregister_RemovesDispatch()
    {
        var source = new FakeHotkeySource();
        var service = new GlobalHotkeyService(new Mock<ILoggingService>().Object, source);
        service.Initialize();
        service.Register(42, 0x0002, 0x4B);
        service.Unregister(42);

        int hitCount = 0;
        service.HotkeyPressed += _ => hitCount++;
        source.Raise(42);

        Assert.Equal(0, hitCount);
    }

    private sealed class FakeHotkeySource : IHotkeySource
    {
        private readonly HashSet<int> _registered = [];
        public event Action<int>? HotkeyPressed;

        public HotkeyRegistrationResult RegisterHotkey(int id, int modifiers, int key, bool consume)
        {
            _registered.Add(id);
            return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Succeeded, id, modifiers, key, null);
        }

        public HotkeyRegistrationResult UnregisterHotkey(int id)
        {
            _registered.Remove(id);
            return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Succeeded, id, 0, 0, null);
        }

        public void Raise(int id)
        {
            if (_registered.Contains(id))
                HotkeyPressed?.Invoke(id);
        }

        public void Dispose()
        {
        }
    }
}
