using Moq;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class SyntheticInputSenderTests
{
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;

    private readonly FakeKeyboardStateReader _keyboard = new();
    private readonly FakeWindowInputApi _windowInputApi = new();
    private readonly FakeTrayWarningSink _trayWarningSink = new();
    private readonly Mock<ILoggingService> _log = new();

    [Fact]
    public void SendPaste_SendInputSucceeds_DoesNotShowTrayWarning()
    {
        _keyboard.DownKeys.Add(VK_CONTROL);
        _windowInputApi.SendInputResult = new SendInputCallResult(2, 0);
        var sender = new SyntheticInputSender(_keyboard, _windowInputApi, _trayWarningSink, _log.Object);

        sender.SendPaste(ClipboardPasteKind.CtrlV);

        Assert.Equal(0, _trayWarningSink.ShowWarningCalls);
    }

    [Fact]
    public void SendPaste_SendInputFails_ShowsTrayWarning()
    {
        _windowInputApi.SendInputResult = new SendInputCallResult(0, 5);
        var sender = new SyntheticInputSender(_keyboard, _windowInputApi, _trayWarningSink, _log.Object);

        sender.SendPaste(ClipboardPasteKind.CtrlV);

        Assert.Equal(1, _trayWarningSink.ShowWarningCalls);
        Assert.Equal("Input injection failed. Press Ctrl+V again manually.", _trayWarningSink.LastText);
    }

    [Fact]
    public void SendPaste_ShiftInsertFailure_ShowsSameTrayWarning()
    {
        _keyboard.DownKeys.Add(VK_SHIFT);
        _windowInputApi.SendInputResult = new SendInputCallResult(0, 5);
        var sender = new SyntheticInputSender(_keyboard, _windowInputApi, _trayWarningSink, _log.Object);

        sender.SendPaste(ClipboardPasteKind.ShiftInsert);

        Assert.Equal(1, _trayWarningSink.ShowWarningCalls);
        Assert.Equal("Input injection failed. Press Ctrl+V again manually.", _trayWarningSink.LastText);
    }

    [Fact]
    public void SendPaste_SendInputFailsTwice_ShowsTrayWarningOnlyOnce()
    {
        _windowInputApi.SendInputResult = new SendInputCallResult(0, 5);
        var sender = new SyntheticInputSender(_keyboard, _windowInputApi, _trayWarningSink, _log.Object);

        sender.SendPaste(ClipboardPasteKind.CtrlV);
        sender.SendPaste(ClipboardPasteKind.CtrlV);

        Assert.Equal(1, _trayWarningSink.ShowWarningCalls);
    }

    [Fact]
    public void SendPaste_SendInputFailsThenSucceeds_DoesNotShowSecondWarning()
    {
        var sender = new SyntheticInputSender(_keyboard, _windowInputApi, _trayWarningSink, _log.Object);
        _windowInputApi.SendInputResult = new SendInputCallResult(0, 5);

        sender.SendPaste(ClipboardPasteKind.CtrlV);

        _windowInputApi.SendInputResult = new SendInputCallResult(2, 0);
        sender.SendPaste(ClipboardPasteKind.CtrlV);

        _windowInputApi.SendInputResult = new SendInputCallResult(0, 5);
        sender.SendPaste(ClipboardPasteKind.CtrlV);

        Assert.Equal(1, _trayWarningSink.ShowWarningCalls);
    }

    private sealed class FakeKeyboardStateReader : IKeyboardStateReader
    {
        public HashSet<int> DownKeys { get; } = [];
        public bool IsKeyDown(int virtualKey) => DownKeys.Contains(virtualKey);
    }

    private sealed class FakeWindowInputApi : IWindowInputApi
    {
        public SendInputCallResult SendInputResult { get; set; }

        public SendInputCallResult SendInput(WindowNative.INPUT[] inputs) => SendInputResult;
    }

    private sealed class FakeTrayWarningSink : ITrayWarningSink
    {
        public int ShowWarningCalls { get; private set; }
        public string LastText { get; private set; } = string.Empty;

        public void ShowWarning(string text)
        {
            ShowWarningCalls++;
            LastText = text;
        }
    }
}
