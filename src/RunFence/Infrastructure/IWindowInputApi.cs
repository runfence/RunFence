namespace RunFence.Infrastructure;

public readonly record struct SendInputCallResult(uint SentCount, int LastError);

public interface IWindowInputApi
{
    SendInputCallResult SendInput(WindowNative.INPUT[] inputs);
}
