namespace RunFence.Infrastructure;

public interface IKeyboardStateReader
{
    bool IsKeyDown(int virtualKey);
}
