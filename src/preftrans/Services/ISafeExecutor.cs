namespace PrefTrans.Services;

public interface ISafeExecutor
{
    void Try(Action action, string operation);
}
