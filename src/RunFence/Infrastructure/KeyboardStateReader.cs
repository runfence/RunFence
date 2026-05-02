namespace RunFence.Infrastructure;

public sealed class KeyboardStateReader : IKeyboardStateReader
{
    public bool IsKeyDown(int virtualKey) =>
        (WindowNative.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
